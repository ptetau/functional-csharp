using System.Collections.Immutable;
using FunctionalLint.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FunctionalLint.Heal;

/// <summary>What one heal run did.</summary>
/// <param name="Solution">The solution with every fix applied.</param>
/// <param name="Fixed">How many problems each rule had fixed.</param>
/// <param name="Remaining">Errors still left (compiler and FunctionalLint), plus FunctionalLint warnings.</param>
/// <param name="NotFixedBecause">Why no fixes were tried, if they weren't.</param>
public sealed record HealResult(Solution Solution, ImmutableSortedDictionary<string, int> Fixed, ImmutableArray<Diagnostic> Remaining,
    string? NotFixedBecause = null);

/// <summary>
/// Applies FunctionalLint's code fixes to a project, one rule at a time, entirely in memory.
/// Each rule's diagnostics are recomputed after the previous rule's fixes, so positions never go stale.
/// </summary>
public static class Healer
{
    public static readonly ImmutableArray<CodeFixProvider> Fixers =
        [new MutableStateFix(), new ClassFix(), new ReadOnlySignatureFix(), new AsyncVoidFix(), new NewLineFix()];

    // The habit that avoids each fixable rule, shown after it's been fixed.
    public static readonly ImmutableDictionary<string, string> Habits = new Dictionary<string, string>
    {
        ["FP0003"] = "Declare fields 'readonly', properties '{ get; init; }', and 'readonly record struct'.",
        ["FP0004"] = "Write records for data and static classes for functions.",
        ["FP0005"] = "Type locals as IReadOnlyList / IReadOnlyDictionary; build with ImmutableArray or ToImmutableArray().",
        ["FP0008"] = "Take and return IReadOnlyList / IReadOnlyDictionary / ImmutableArray, never List or IList.",
        ["FP0010"] = "Make async methods return Task, never void.",
        ["FP0011"] = "When you set WriteIndented, set NewLine = \"\\n\" too, so the output is the same on every OS.",
    }.ToImmutableDictionary();

    private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers = [new FunctionalAnalyzer()];

    /// <summary>
    /// Parse with the newest language version this tool knows. The Roslyn package on NuGet can lag the
    /// compiler in the SDK (C# 15 shipped as "preview" in Roslyn 5.9), and parsing new syntax with an older
    /// language version misreads it: `closed record Shape;` would look like a field named `record`.
    /// Only document text is ever written back, so this setting never reaches the project file.
    /// </summary>
    public static Project UseNewestLanguage(Project project) =>
        project.ParseOptions is CSharpParseOptions options && options.LanguageVersion != LanguageVersion.Preview
            ? project.WithParseOptions(options.WithLanguageVersion(LanguageVersion.Preview))
            : project;

    public static async Task<HealResult> HealAsync(Project project, CancellationToken ct = default)
    {
        project = UseNewestLanguage(project);
        var before = await DiagnoseAsync(project, ct);

        // A fix computed on code we can't parse could rewrite the wrong thing. Report, don't touch.
        var syntaxErrors = await SyntaxErrorsAsync(project, ct);
        if (syntaxErrors > 0)
            return new HealResult(project.Solution, ImmutableSortedDictionary<string, int>.Empty, Sorted(before),
                $"{syntaxErrors} syntax error(s): fix those first, then run heal again.");

        // Fixing one rule can expose another (a class becomes a record, its setters are then FP0003),
        // so go round until nothing changes. Three passes is plenty in practice.
        for (var pass = 0; pass < 3; pass++)
        {
            var changed = false;
            foreach (var fixer in Fixers)
                foreach (var id in fixer.FixableDiagnosticIds)
                {
                    var next = await FixRuleAsync(project, fixer, id, ct);
                    if (next is null) continue;
                    project = next;
                    changed = true;
                }
            if (!changed) break;
        }

        var after = await DiagnoseAsync(project, ct);

        var fixedCounts = Fixers.SelectMany(f => f.FixableDiagnosticIds).Distinct()
            .Select(id => (id, n: Count(before, id) - Count(after, id)))
            .Where(x => x.n > 0)
            .ToImmutableSortedDictionary(x => x.id, x => x.n);

        return new HealResult(project.Solution, fixedCounts, Sorted(after));
    }

    private static ImmutableArray<Diagnostic> Sorted(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error || IsOurs(d))
            .OrderBy(d => d.Location.SourceTree?.FilePath).ThenBy(d => d.Location.SourceSpan.Start)
            .ToImmutableArray();

    private static async Task<int> SyntaxErrorsAsync(Project project, CancellationToken ct)
    {
        var trees = await Task.WhenAll(project.Documents.Select(d => d.GetSyntaxTreeAsync(ct)));
        return trees.Sum(t => t?.GetDiagnostics(ct).Count(d => d.Severity == DiagnosticSeverity.Error) ?? 0);
    }

    /// <summary>
    /// What the build would report: compiler errors, FunctionalLint's diagnostics, and errors from the project's
    /// other analyzers (such as the .NET culture rules FunctionalCore.props turns on), using the project's own
    /// config (.editorconfig, .globalconfig).
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> DiagnoseAsync(Project project, CancellationToken ct = default)
    {
        var compilation = await project.GetCompilationAsync(ct)
            ?? throw new InvalidOperationException($"Couldn't compile {project.Name}.");
        var compilerErrors = compilation.GetDiagnostics(ct).Where(d => d.Severity == DiagnosticSeverity.Error);
        var analyzers = Analyzers.AddRange(ProjectAnalyzers(project));

        // Under a policy, its values replace the project's for the policy keys, and the strict gate checks run.
        var policy = Policy.For(project);
        var options = policy?.Apply(project.AnalyzerOptions) ?? project.AnalyzerOptions;
        var found = await compilation.WithAnalyzers(analyzers, options).GetAnalyzerDiagnosticsAsync(ct);
        var gate = policy is null ? [] : Gate.Check(project, compilation, policy, ct).ToList();
        // The gate repeats the build's in-file FP0016 scan (a pragma can't hide it there), so drop repeats.
        return [.. compilerErrors, .. found.Where(d => IsOurs(d) ? d.Severity != DiagnosticSeverity.Hidden : d.Severity == DiagnosticSeverity.Error)
            .Concat(gate).DistinctBy(d => (d.Id, d.Location.SourceTree?.FilePath, d.Location.SourceSpan, d.GetMessage()))];
    }

    // The project's own analyzers, minus FunctionalLint (this tool runs its own copy, so each problem shows once).
    // An analyzer this tool's Roslyn can't load is skipped: the build still runs it.
    private static IEnumerable<DiagnosticAnalyzer> ProjectAnalyzers(Project project) =>
        project.AnalyzerReferences
            .Where(r => !(r.FullPath ?? r.Display ?? "").Contains("FunctionalLint", StringComparison.OrdinalIgnoreCase))
            .SelectMany(r =>
            {
                try { return r.GetAnalyzers(LanguageNames.CSharp); }
                catch (Exception) { return ImmutableArray<DiagnosticAnalyzer>.Empty; }
            });

    /// <summary>
    /// Fixes every diagnostic of one rule, but never makes the build worse. If fixing them all at once adds a
    /// compiler error (say, `IReadOnlyList&lt;int&gt; xs = new();`, or a field made readonly that's assigned
    /// elsewhere), fix them one at a time instead and keep only the fixes that add no error. A fix that's thrown
    /// away leaves its diagnostic in the report, so it isn't counted as fixed.
    /// </summary>
    private static async Task<Project?> FixRuleAsync(Project project, CodeFixProvider fixer, string id, CancellationToken ct)
    {
        var baseline = await CompilerErrorsAsync(project, ct);
        var all = await FixAllAsync(project, fixer, id, ct);
        if (all is null) return null;
        if (await CompilerErrorsAsync(all, ct) <= baseline) return all;

        var tried = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;
        while (true)
        {
            var next = (await DiagnoseAsync(project, ct))
                .FirstOrDefault(d => d.Id == id && d.Location.IsInSource && !tried.Contains(Key(d)));
            if (next is null) break;
            tried.Add(Key(next));

            var candidate = await FixOneAsync(project, fixer, next, ct);
            if (candidate is null || await CompilerErrorsAsync(candidate, ct) > baseline) continue;
            project = candidate;
            changed = true;
        }
        return changed ? project : null;
    }

    // Identifies a diagnostic across edits elsewhere in the file, which move its position.
    private static string Key(Diagnostic d) =>
        $"{d.Location.SourceTree?.FilePath}|{d.GetMessage()}|{d.Location.SourceTree?.GetText().ToString(d.Location.SourceSpan)}";

    // Compiler errors in the project and in every project that depends on it: turning `set` into `init` compiles
    // in the project itself but breaks a host project that sets the property.
    private static async Task<int> CompilerErrorsAsync(Project project, CancellationToken ct)
    {
        var solution = project.Solution;
        var affected = solution.GetProjectDependencyGraph()
            .GetProjectsThatTransitivelyDependOnThisProject(project.Id)
            .Prepend(project.Id);
        var total = 0;
        foreach (var id in affected)
        {
            var compilation = await solution.GetProject(id)!.GetCompilationAsync(ct);
            total += compilation?.GetDiagnostics(ct).Count(d => d.Severity == DiagnosticSeverity.Error) ?? 0;
        }
        return total;
    }

    private static async Task<Project?> FixOneAsync(Project project, CodeFixProvider fixer, Diagnostic diagnostic, CancellationToken ct)
    {
        if (project.GetDocument(diagnostic.Location.SourceTree) is not { } document) return null;
        var actions = new List<CodeAction>();
        await fixer.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), ct));
        if (actions.FirstOrDefault() is not { } action) return null;

        var operations = await action.GetOperationsAsync(ct);
        return operations.OfType<ApplyChangesOperation>().FirstOrDefault()?.ChangedSolution.GetProject(project.Id);
    }

    private static async Task<Project?> FixAllAsync(
        Project project, CodeFixProvider fixer, string id, CancellationToken ct, ImmutableArray<Diagnostic>? known = null)
    {
        var diagnostics = (known ?? await DiagnoseAsync(project, ct)).Where(d => d.Id == id && d.Location.IsInSource).ToImmutableArray();
        if (diagnostics.IsEmpty) return null;

        // Find one diagnostic the fixer offers an action for; fix-all then applies that action everywhere.
        foreach (var diagnostic in diagnostics)
        {
            var document = project.GetDocument(diagnostic.Location.SourceTree);
            if (document is null) continue;

            var actions = new List<CodeAction>();
            await fixer.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), ct));
            if (actions.FirstOrDefault()?.EquivalenceKey is not { } key) continue;

            var context = new FixAllContext(document, fixer, FixAllScope.Project, key, [id], new Provider(diagnostics), ct);
            var fixAll = await fixer.GetFixAllProvider()!.GetFixAsync(context);
            if (fixAll is null) return null;

            var operations = await fixAll.GetOperationsAsync(ct);
            var solution = operations.OfType<ApplyChangesOperation>().FirstOrDefault()?.ChangedSolution;
            return solution?.GetProject(project.Id);
        }
        return null;
    }

    /// <summary>
    /// The diagnostics heal would really fix: a fix is offered for them, and fixing their whole rule at once adds no
    /// compiler error (here or in projects that depend on this one). That's one fix-all and one compile per rule
    /// present, not a compile per diagnostic, so it's cheap enough for every --check and hook run. It's conservative:
    /// if a rule's fix-all would break the build, none of its diagnostics are marked, even though heal may still
    /// fix some of them one at a time.
    /// </summary>
    /// <remarks>
    /// When a rule's fix-all would break the build, the diagnostics in <paramref name="focus"/> (the file just edited)
    /// are tried one at a time, at most ten, so one bad case doesn't hide the fixable ones an agent is looking at.
    /// </remarks>
    public static async Task<HashSet<Diagnostic>> FixableAsync(
        Project project, ImmutableArray<Diagnostic> diagnostics, string? focus = null, CancellationToken ct = default)
    {
        var fixable = new HashSet<Diagnostic>();
        int? baseline = null;
        foreach (var fixer in Fixers)
            foreach (var id in fixer.FixableDiagnosticIds)
            {
                var ofRule = diagnostics.Where(d => d.Id == id && d.Location.IsInSource).ToImmutableArray();
                var offered = new List<Diagnostic>();
                foreach (var diagnostic in ofRule)
                    if (await OffersFixAsync(project, fixer, diagnostic, ct)) offered.Add(diagnostic);
                if (offered.Count == 0) continue;

                if (await FixAllAsync(project, fixer, id, ct, ofRule) is not { } fixedProject) continue;
                baseline ??= await CompilerErrorsAsync(project, ct);
                if (await CompilerErrorsAsync(fixedProject, ct) <= baseline)
                {
                    fixable.UnionWith(offered);
                    continue;
                }

                foreach (var diagnostic in offered.Where(d => focus is not null && d.Location.SourceTree?.FilePath == focus).Take(10))
                    if (await FixOneAsync(project, fixer, diagnostic, ct) is { } one && await CompilerErrorsAsync(one, ct) <= baseline)
                        fixable.Add(diagnostic);
            }
        return fixable;
    }

    public static async Task<bool> IsFixableAsync(Project project, Diagnostic diagnostic, CancellationToken ct = default) =>
        (await FixableAsync(project, [diagnostic], diagnostic.Location.SourceTree?.FilePath, ct)).Count > 0;

    private static async Task<bool> OffersFixAsync(Project project, CodeFixProvider fixer, Diagnostic diagnostic, CancellationToken ct)
    {
        if (project.GetDocument(diagnostic.Location.SourceTree) is not { } document) return false;
        var offered = false;
        await fixer.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (_, _) => offered = true, ct));
        return offered;
    }

    private static int Count(ImmutableArray<Diagnostic> diagnostics, string id) => diagnostics.Count(d => d.Id == id);

    public static bool IsOurs(Diagnostic d) => d.Id.StartsWith("FP", StringComparison.Ordinal);

    private sealed class Provider(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken ct)
        {
            var tree = await document.GetSyntaxTreeAsync(ct);
            return diagnostics.Where(d => d.Location.SourceTree == tree);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken ct) =>
            Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken ct) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}
