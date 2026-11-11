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
        [new MutableStateFix(), new ClassFix(), new ReadOnlySignatureFix(), new AsyncVoidFix()];

    // The habit that avoids each fixable rule, shown after it's been fixed.
    public static readonly ImmutableDictionary<string, string> Habits = new Dictionary<string, string>
    {
        ["FP0003"] = "Declare fields 'readonly', properties '{ get; init; }', and 'readonly record struct'.",
        ["FP0004"] = "Write records for data and static classes for functions.",
        ["FP0005"] = "Type locals as IReadOnlyList / IReadOnlyDictionary; build with ImmutableArray or ToImmutableArray().",
        ["FP0008"] = "Take and return IReadOnlyList / IReadOnlyDictionary / ImmutableArray, never List or IList.",
        ["FP0010"] = "Make async methods return Task, never void.",
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
                    var next = await FixAllAsync(project, fixer, id, ct);
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

    /// <summary>Compiler errors plus FunctionalLint's diagnostics, using the project's own config (.editorconfig, .globalconfig).</summary>
    public static async Task<ImmutableArray<Diagnostic>> DiagnoseAsync(Project project, CancellationToken ct = default)
    {
        var compilation = await project.GetCompilationAsync(ct)
            ?? throw new InvalidOperationException($"Couldn't compile {project.Name}.");
        var compilerErrors = compilation.GetDiagnostics(ct).Where(d => d.Severity == DiagnosticSeverity.Error);
        var ours = await compilation.WithAnalyzers(Analyzers, project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync(ct);
        return [.. compilerErrors, .. ours.Where(d => d.Severity != DiagnosticSeverity.Hidden)];
    }

    private static async Task<Project?> FixAllAsync(Project project, CodeFixProvider fixer, string id, CancellationToken ct)
    {
        var diagnostics = (await DiagnoseAsync(project, ct)).Where(d => d.Id == id && d.Location.IsInSource).ToImmutableArray();
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

    /// <summary>True if one of the fixes would change the code for this diagnostic.</summary>
    public static async Task<bool> IsFixableAsync(Project project, Diagnostic diagnostic, CancellationToken ct = default)
    {
        if (project.GetDocument(diagnostic.Location.SourceTree) is not { } document) return false;
        foreach (var fixer in Fixers.Where(f => f.FixableDiagnosticIds.Contains(diagnostic.Id)))
        {
            var offered = false;
            await fixer.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (_, _) => offered = true, ct));
            if (offered) return true;
        }
        return false;
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
