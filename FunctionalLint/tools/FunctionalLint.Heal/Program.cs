using System.Diagnostics;
using FunctionalLint.Heal;

// Usage:
//   heal <project.csproj | solution.sln(x)>          fix what can be fixed, report the rest
//   heal <project.csproj | solution.sln(x)> --check  report only; change nothing
//   heal --hook                                       agent hook: read the edited file from stdin JSON,
//                                                     check its project, report problems on stderr
//   heal --serve                                      keep projects loaded so checks take well under a second
// Exit codes: 0 clean, 1 problems remain, 2 couldn't run. In --hook mode problems exit 2,
// which is how Claude Code hooks hand stderr back to the agent.
//
// --check and --hook ask a running check server first, and fall back to checking here. --hook also starts the
// server in the background for next time. FUNCTIONALLINT_NO_SERVER=1 turns the server off.

var cwd = Environment.CurrentDirectory;

if (args.Contains("--serve")) return await Server.RunAsync();

if (args.Contains("--hook"))
{
    var edited = Hook.EditedFile(await Console.In.ReadToEndAsync());
    if (edited is null || !edited.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return 0;
    if (Hook.ProjectFor(edited) is not { } owner) return 0;

    var (code, report) = await CheckAsync(owner, Path.GetFullPath(edited), startServer: true);
    if (code is 0 or 3) return 0;   // clean, or not a functional project
    Console.Error.Write(report);
    return 2;
}

var path = args.FirstOrDefault(a => !a.StartsWith("--"));
if (path is null || !File.Exists(path))
{
    Console.Error.WriteLine("usage: heal <project.csproj | solution.sln> [--check]   or   heal --hook   or   heal --serve");
    return 2;
}

var (exit, output) = args.Contains("--check")
    ? await CheckAsync(path, focus: null, startServer: false)
    : await HealAsync(path);
Console.Write(output);
return exit == 3 ? 2 : exit;

async Task<(int Code, string Report)> CheckAsync(string target, string? focus, bool startServer)
{
    // Solutions are checked here: the server keeps single projects, which is what hooks ask for.
    if (!target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        return await CheckHereAsync(target, focus);

    if (await Server.TryAskAsync(new Server.Request(target, focus, cwd), TimeSpan.FromSeconds(60)) is { } answer)
        return (answer.Code, answer.Report);

    var result = await CheckHereAsync(target, focus);
    if (startServer) Server.StartInBackground();
    return result;
}

async Task<(int Code, string Report)> CheckHereAsync(string target, string? focus)
{
    var clock = Stopwatch.StartNew();
    var loaded = await Checker.LoadAsync(target);
    using (loaded.Workspace)
        return await Checker.CheckAsync(loaded.Workspace.CurrentSolution, loaded.Functional, focus, cwd, clock);
}

async Task<(int Code, string Report)> HealAsync(string target)
{
    var clock = Stopwatch.StartNew();
    var loaded = await Checker.LoadAsync(target);
    using (loaded.Workspace)
        return await Checker.HealAsync(loaded, cwd, clock);
}
