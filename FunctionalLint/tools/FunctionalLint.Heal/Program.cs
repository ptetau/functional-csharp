using System.Diagnostics;
using FunctionalLint.Heal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

// Usage: heal <project.csproj | solution.sln(x)> [--check]
//   --check   report only; don't change any files.
// Exit codes: 0 clean, 1 problems remain, 2 couldn't run.

var path = args.FirstOrDefault(a => !a.StartsWith("--"));
var checkOnly = args.Contains("--check");
if (path is null || !File.Exists(path))
{
    Console.Error.WriteLine("usage: heal <project.csproj | solution.sln> [--check]");
    return 2;
}

var clock = Stopwatch.StartNew();
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
    Console.Error.WriteLine($"No project in {path} references FunctionalLint. Import FunctionalCore.props first.");
    return 2;
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
    Console.Error.WriteLine("Couldn't write the fixes to disk.");
    return 2;
}

if (!checkOnly)
{
    Console.WriteLine("== Auto-fixed");
    if (fixedTotals.Count == 0) Console.WriteLine("  nothing");
    foreach (var (rule, n) in fixedTotals)
        Console.WriteLine($"  {rule} x{n}. Next time: {Healer.Habits.GetValueOrDefault(rule, "")}");
    Console.WriteLine();
}

var lines = remaining
    .OrderBy(r => r.Diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
    .ThenBy(r => r.Diagnostic.Location.SourceSpan.Start)
    .Select(r => Format(r.Diagnostic) + (r.Fixable ? " [auto-fixable]" : ""))
    .Distinct()
    .ToList();

if (lines.Count == 0)
{
    Console.WriteLine($"== Clean. ({clock.Elapsed.TotalSeconds:0.0}s)");
    return 0;
}

var fixable = remaining.Count(r => r.Fixable);
Console.WriteLine(checkOnly
    ? $"== {lines.Count} problem(s), {fixable} auto-fixable with heal.sh ({clock.Elapsed.TotalSeconds:0.0}s)"
    : $"== Remaining: {lines.Count} to fix by hand ({clock.Elapsed.TotalSeconds:0.0}s)");
lines.ForEach(Console.WriteLine);
Console.WriteLine();
Console.WriteLine("Each message says what to write instead. Rule docs with examples: FunctionalLint/docs/rules/<ID>.md");
return 1;

static string Format(Diagnostic d)
{
    var span = d.Location.GetLineSpan();
    var file = span.Path is { Length: > 0 } p ? Path.GetRelativePath(Environment.CurrentDirectory, p) : "";
    var severity = d.Severity == DiagnosticSeverity.Error ? "error" : "warning";
    var position = $"({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
    return $"{file}{position}: {severity} {d.Id}: {d.GetMessage()}";
}
