using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace FunctionalLint.Heal;

/// <summary>Loads projects and turns their diagnostics into the report heal prints. Shared by the CLI and the server.</summary>
public static class Checker
{
    private const string DocsHint = "Each message says what to write instead. Rule docs with examples: FunctionalLint/docs/rules/<ID>.md";

    /// <summary>A loaded project or solution, with the projects that use FunctionalLint, and anything to tell the user.</summary>
    public sealed record Loaded(MSBuildWorkspace Workspace, IReadOnlyList<ProjectId> Functional, string? Note = null);

    /// <summary>
    /// Loads a project together with the projects that depend on it: the nearest solution above it that lists it.
    /// Fixes are then checked against the dependents too (turning `set` into `init` compiles in a project but breaks
    /// a host that sets the property). Only the requested project is checked or fixed. Without such a solution, the
    /// project is loaded alone and the result carries a note saying dependents weren't checked.
    /// </summary>
    public static async Task<Loaded> LoadWithDependentsAsync(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return await LoadAsync(full);

        const string unchecked_ = "so projects that depend on it weren't checked. Heal the solution instead to be sure a fix doesn't break them.";
        if (SolutionListing(full) is { } solution)
        {
            var loaded = await LoadAsync(solution);
            var only = loaded.Functional
                .Where(id => loaded.Workspace.CurrentSolution.GetProject(id)?.FilePath is { } file && SamePath(file, full))
                .ToList();
            if (only.Count > 0) return loaded with { Functional = only };
            loaded.Workspace.Dispose();
        }

        var alone = await LoadAsync(full);
        return alone with { Note = $"No solution file above {Path.GetFileName(full)} lists it, {unchecked_}" };
    }

    // File systems on Windows and macOS ignore case by default; Linux's don't.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathComparison);

    // The nearest .sln or .slnx at or above the project's folder that lists this exact project.
    private static string? SolutionListing(string project)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(project)!); dir is not null; dir = dir.Parent)
        {
            var match = dir.EnumerateFiles("*.sln").Concat(dir.EnumerateFiles("*.slnx"))
                .FirstOrDefault(f => ProjectsIn(f.FullName).Any(listed => SamePath(listed, project)));
            if (match is not null) return match.FullName;
        }
        return null;
    }

    private static readonly System.Text.RegularExpressions.Regex SlnProjectLine =
        new("^Project\\(\"[^\"]*\"\\)\\s*=\\s*\"[^\"]*\",\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.Multiline);

    // The full paths of the projects a solution lists.
    private static IEnumerable<string> ProjectsIn(string solution)
    {
        var folder = Path.GetDirectoryName(solution)!;
        try
        {
            var relative = solution.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                ? System.Xml.Linq.XDocument.Load(solution).Descendants()
                    .Where(e => e.Name.LocalName == "Project").Select(e => e.Attribute("Path")?.Value).OfType<string>()
                : SlnProjectLine.Matches(File.ReadAllText(solution)).Select(m => m.Groups[1].Value);
            return relative.Select(r => Path.GetFullPath(r.Replace('\\', Path.DirectorySeparatorChar), folder)).ToList();
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The projects in <paramref name="path"/> that reference FunctionalLint. Host, test and adapter projects are left alone, as at build time.</summary>
    public static async Task<Loaded> LoadAsync(string path)
    {
        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) Console.Error.WriteLine($"warning: {e.Diagnostic.Message}");
        });

        var full = Path.GetFullPath(path);
        var projects = Path.GetExtension(full) is ".sln" or ".slnx"
            ? (await workspace.OpenSolutionAsync(full)).Projects.ToList()
            : [await workspace.OpenProjectAsync(full)];

        // A policy core counts even if it no longer loads FunctionalLint: the gate checks it, and says so.
        var functional = projects
            .Where(p => UsesFunctionalLint(p) || Policy.For(p)?.IsCore(p.FilePath) == true)
            .Select(p => p.Id)
            .ToList();
        return new Loaded(workspace, functional);
    }

    public static bool UsesFunctionalLint(Project project) =>
        project.AnalyzerReferences.Any(r => (r.FullPath ?? r.Display).Contains("FunctionalLint", StringComparison.OrdinalIgnoreCase));

    // What the gate adds for the project as a whole, beyond its diagnostics.
    private static IEnumerable<Diagnostic> ProjectGate(Project project) =>
        !UsesFunctionalLint(project) && Policy.For(project) is { } policy
        && policy.IsCore(project.FilePath) && !policy.IsGateOnly(project.FilePath) ? [Gate.MissingAnalyzer(project)] : [];

    /// <summary>0 clean, 1 problems, 3 nothing uses FunctionalLint.</summary>
    public static async Task<(int Code, string Report)> CheckAsync(
        Solution solution, IReadOnlyList<ProjectId> functional, string? focus, string cwd, Stopwatch clock)
    {
        if (functional.Count == 0) return (3, "No project here references FunctionalLint. Import FunctionalCore.props first.\n");

        var remaining = new List<(Diagnostic Diagnostic, bool Fixable)>();
        foreach (var id in functional)
        {
            var project = Healer.UseNewestLanguage(solution.GetProject(id)!);
            var found = (await Healer.DiagnoseAsync(project))
                .Where(d => d.Severity == DiagnosticSeverity.Error || Healer.IsOurs(d)).ToImmutableArray();
            var fixable = await Healer.FixableAsync(project, found, focus);
            remaining.AddRange(found.Select(d => (d, fixable.Contains(d))));
            remaining.AddRange(ProjectGate(project).Select(d => (d, false)));
        }
        return Report(remaining, checkOnly: true, focus, cwd, clock, prefix: "");
    }

    /// <summary>Fixes what can be fixed, writes the files, and reports the rest. 0 clean, 1 problems, 2 failed, 3 nothing uses FunctionalLint.</summary>
    public static async Task<(int Code, string Report)> HealAsync(Loaded loaded, string cwd, Stopwatch clock)
    {
        if (loaded.Functional.Count == 0) return (3, "No project here references FunctionalLint. Import FunctionalCore.props first.\n");

        // Only edited document text is written back. Healing also changes the language version it parses with
        // (see Healer.UseNewestLanguage), and that must never reach the project.
        var solution = loaded.Workspace.CurrentSolution;
        var fixedTotals = new SortedDictionary<string, int>();
        var remaining = new List<(Diagnostic Diagnostic, bool Fixable)>();
        var notFixed = new List<string>();

        foreach (var id in loaded.Functional)
        {
            var project = Healer.UseNewestLanguage(solution.GetProject(id)!);
            var result = await Healer.HealAsync(project);
            foreach (var changed in result.Solution.GetChanges(project.Solution).GetProjectChanges().SelectMany(c => c.GetChangedDocuments()))
                solution = solution.WithDocumentText(changed, await result.Solution.GetDocument(changed)!.GetTextAsync());
            if (result.NotFixedBecause is { } why) notFixed.Add($"{project.Name}: not fixed, {why}");
            foreach (var (rule, n) in result.Fixed) fixedTotals[rule] = fixedTotals.GetValueOrDefault(rule) + n;
            remaining.AddRange(result.Remaining.Select(d => (d, false)));
            remaining.AddRange(ProjectGate(project).Select(d => (d, false)));
        }

        if (!loaded.Workspace.TryApplyChanges(solution)) return (2, "Couldn't write the fixes to disk.\n");

        var header = new StringWriter();
        if (loaded.Note is { } note) header.WriteLine("note: " + note);
        header.WriteLine("== Auto-fixed");
        notFixed.ForEach(line => header.WriteLine("  " + line));
        if (fixedTotals.Count == 0 && notFixed.Count == 0) header.WriteLine("  nothing");
        foreach (var (rule, n) in fixedTotals)
            header.WriteLine($"  {rule} x{n}. Next time: {Healer.Habits.GetValueOrDefault(rule, "")}");
        header.WriteLine();

        return Report(remaining, checkOnly: false, focus: null, cwd, clock, header.ToString());
    }

    private static (int Code, string Report) Report(
        List<(Diagnostic Diagnostic, bool Fixable)> remaining, bool checkOnly, string? focus, string cwd, Stopwatch clock, string prefix)
    {
        var report = new StringWriter();
        report.Write(prefix);

        // The edited file's problems first (hook mode), then everything else in file order.
        var ordered = remaining
            .OrderBy(r => r.Diagnostic.Location.SourceTree?.FilePath == focus ? 0 : 1)
            .ThenBy(r => r.Diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(r => r.Diagnostic.Location.SourceSpan.Start)
            .Select(r => (Line: Format(r.Diagnostic, cwd) + (r.Fixable ? " [auto-fixable]" : ""), r.Fixable,
                          InFocus: r.Diagnostic.Location.SourceTree?.FilePath == focus, IsOurs: Healer.IsOurs(r.Diagnostic),
                          IsError: r.Diagnostic.Severity == DiagnosticSeverity.Error))
            .DistinctBy(r => r.Line)
            .ToList();

        if (ordered.Count == 0)
        {
            report.WriteLine($"== Clean. ({clock.Elapsed.TotalSeconds:0.0}s)");
            return (0, report.ToString());
        }

        var fixable = ordered.Count(r => r.Fixable);
        if (focus is not null)
        {
            // An agent wants the file it just touched; cap the rest so the message stays readable.
            var here = ordered.Where(r => r.InFocus).ToList();
            var elsewhere = ordered.Where(r => !r.InFocus).ToList();

            // Only fail the edit for the file just touched, or for errors elsewhere. Warnings elsewhere (a rule
            // lowered to "warning" while migrating) don't fail the build, so they mustn't fail every edit either.
            if (here.Count == 0 && !elsewhere.Any(r => r.IsError))
                return (0, report.ToString());
            report.WriteLine($"FunctionalLint: {here.Count} problem(s) in {Path.GetFileName(focus)}, {elsewhere.Count} elsewhere in the project.");
            here.ForEach(r => report.WriteLine(r.Line));
            elsewhere.Take(10).ToList().ForEach(r => report.WriteLine(r.Line));
            if (elsewhere.Count > 10) report.WriteLine($"... and {elsewhere.Count - 10} more.");
            if (fixable > 0) report.WriteLine($"{fixable} marked [auto-fixable]: run FunctionalLint/heal.sh <project> to fix them, then fix the rest by hand.");
            if (ordered.Any(r => r.IsOurs)) report.WriteLine(DocsHint);
            return (1, report.ToString());
        }

        report.WriteLine(checkOnly
            ? $"== {ordered.Count} problem(s), {fixable} auto-fixable with heal.sh ({clock.Elapsed.TotalSeconds:0.0}s)"
            : $"== Remaining: {ordered.Count} to fix by hand ({clock.Elapsed.TotalSeconds:0.0}s)");
        ordered.ForEach(r => report.WriteLine(r.Line));
        if (ordered.Any(r => r.IsOurs))
        {
            report.WriteLine();
            report.WriteLine(DocsHint);
        }
        return (1, report.ToString());
    }

    private static string Format(Diagnostic d, string cwd)
    {
        var span = d.Location.GetLineSpan();
        // Relative to where you ran it when that's shorter and clearer; absolute otherwise.
        var relative = span.Path is { Length: > 0 } p ? Path.GetRelativePath(cwd, p) : "";
        var file = relative.StartsWith("..", StringComparison.Ordinal) ? span.Path : relative;
        var severity = d.Severity == DiagnosticSeverity.Error ? "error" : "warning";
        // Project-wide findings (a lowered rule, a missing import) have no file.
        if (!d.Location.IsInSource && span.Path is not { Length: > 0 }) return $"{severity} {d.Id}: {d.GetMessage()}";
        return $"{file}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {severity} {d.Id}: {d.GetMessage()}";
    }
}
