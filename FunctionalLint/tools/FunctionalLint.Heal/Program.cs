using System.Diagnostics;
using FunctionalLint.Heal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

// Usage:
//   heal <project.csproj | solution.sln(x)>          fix what can be fixed, report the rest
//   heal <project.csproj | solution.sln(x)> --check  report only; change nothing
//   heal --hook                                       agent hook: read the edited file from stdin JSON,
//                                                     check its project, report problems on stderr
// Exit codes: 0 clean, 1 problems remain, 2 couldn't run. In --hook mode problems exit 2,
// which is how Claude Code hooks hand stderr back to the agent.

const string DocsHint = "Each message says what to write instead. Rule docs with examples: FunctionalLint/docs/rules/<ID>.md";

if (args.Contains("--hook"))
{
    var edited = Hook.EditedFile(await Console.In.ReadToEndAsync());
    if (edited is null || !edited.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return 0;
    if (Hook.ProjectFor(edited) is not { } owner) return 0;

    var (code, report) = await Run(owner, checkOnly: true, focus: Path.GetFullPath(edited));
    if (code == 0 || code == 3) return 0;   // clean, or not a functional project
    Console.Error.Write(report);
    return 2;
}

var path = args.FirstOrDefault(a => !a.StartsWith("--"));
if (path is null || !File.Exists(path))
{
    Console.Error.WriteLine("usage: heal <project.csproj | solution.sln> [--check]   or   heal --hook");
    return 2;
}

var (exit, output) = await Run(path, args.Contains("--check"), focus: null);
Console.Write(output);
return exit == 3 ? 2 : exit;

// 0 clean, 1 problems, 2 failed, 3 no project references FunctionalLint.
static async Task<(int Code, string Report)> Run(string path, bool checkOnly, string? focus)
{
    var clock = Stopwatch.StartNew();
    var report = new StringWriter();

    using var workspace = MSBuildWorkspace.Create();
    workspace.RegisterWorkspaceFailedHandler(e =>
    {
        if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) Console.Error.WriteLine($"warning: {e.Diagnostic.Message}");
    });

    var full = Path.GetFullPath(path);
    var projects = Path.GetExtension(full) is ".sln" or ".slnx"
        ? (await workspace.OpenSolutionAsync(full)).Projects.ToList()
        : [await workspace.OpenProjectAsync(full)];

    // Only heal projects that opted in by referencing the analyzer (through FunctionalCore.props or the package).
    // Host, test and adapter projects are left alone, as they are at build time.
    var functional = projects
        .Where(p => p.AnalyzerReferences.Any(r => (r.FullPath ?? r.Display).Contains("FunctionalLint", StringComparison.OrdinalIgnoreCase)))
        .ToList();
    if (functional.Count == 0)
    {
        report.WriteLine($"No project in {path} references FunctionalLint. Import FunctionalCore.props first.");
        return (3, report.ToString());
    }

    var solution = workspace.CurrentSolution;
    var fixedTotals = new SortedDictionary<string, int>();
    var remaining = new List<(Diagnostic Diagnostic, bool Fixable)>();

    foreach (var id in functional.Select(p => p.Id))
    {
        var project = solution.GetProject(id)!;
        if (checkOnly)
        {
            var found = await Healer.DiagnoseAsync(project);
            foreach (var d in found.Where(d => d.Severity == DiagnosticSeverity.Error || Healer.IsOurs(d)))
                remaining.Add((d, await Healer.IsFixableAsync(project, d)));
            continue;
        }

        var result = await Healer.HealAsync(project);
        solution = result.Solution;
        foreach (var (rule, n) in result.Fixed) fixedTotals[rule] = fixedTotals.GetValueOrDefault(rule) + n;
        remaining.AddRange(result.Remaining.Select(d => (d, false)));
    }

    if (!checkOnly && !workspace.TryApplyChanges(solution))
    {
        report.WriteLine("Couldn't write the fixes to disk.");
        return (2, report.ToString());
    }

    if (!checkOnly)
    {
        report.WriteLine("== Auto-fixed");
        if (fixedTotals.Count == 0) report.WriteLine("  nothing");
        foreach (var (rule, n) in fixedTotals)
            report.WriteLine($"  {rule} x{n}. Next time: {Healer.Habits.GetValueOrDefault(rule, "")}");
        report.WriteLine();
    }

    // The edited file's problems first (hook mode), then everything else in file order.
    var ordered = remaining
        .OrderBy(r => r.Diagnostic.Location.SourceTree?.FilePath == focus ? 0 : 1)
        .ThenBy(r => r.Diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
        .ThenBy(r => r.Diagnostic.Location.SourceSpan.Start)
        .Select(r => (Line: Format(r.Diagnostic) + (r.Fixable ? " [auto-fixable]" : ""), r.Fixable,
                      InFocus: r.Diagnostic.Location.SourceTree?.FilePath == focus))
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
        report.WriteLine($"FunctionalLint: {here.Count} problem(s) in {Path.GetFileName(focus)}, {elsewhere.Count} elsewhere in the project.");
        here.ForEach(r => report.WriteLine(r.Line));
        elsewhere.Take(10).ToList().ForEach(r => report.WriteLine(r.Line));
        if (elsewhere.Count > 10) report.WriteLine($"... and {elsewhere.Count - 10} more.");
        if (fixable > 0) report.WriteLine($"{fixable} marked [auto-fixable]: run FunctionalLint/heal.sh <project> to fix them, then fix the rest by hand.");
        report.WriteLine(DocsHint);
        return (1, report.ToString());
    }

    report.WriteLine(checkOnly
        ? $"== {ordered.Count} problem(s), {fixable} auto-fixable with heal.sh ({clock.Elapsed.TotalSeconds:0.0}s)"
        : $"== Remaining: {ordered.Count} to fix by hand ({clock.Elapsed.TotalSeconds:0.0}s)");
    ordered.ForEach(r => report.WriteLine(r.Line));
    report.WriteLine();
    report.WriteLine(DocsHint);
    return (1, report.ToString());
}

static string Format(Diagnostic d)
{
    var span = d.Location.GetLineSpan();
    // Relative to where you ran it when that's shorter and clearer; absolute otherwise.
    var relative = span.Path is { Length: > 0 } p ? Path.GetRelativePath(Environment.CurrentDirectory, p) : "";
    var file = relative.StartsWith("..", StringComparison.Ordinal) ? span.Path : relative;
    var severity = d.Severity == DiagnosticSeverity.Error ? "error" : "warning";
    return $"{file}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {severity} {d.Id}: {d.GetMessage()}";
}
