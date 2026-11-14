using System.Diagnostics;
using FunctionalLint.Heal;

// Usage:
//   heal <project.csproj | solution.sln(x)>          fix what can be fixed, report the rest
//   heal <project.csproj | solution.sln(x)> --check  report only; change nothing
//   heal --hook                                       agent hook: read the edited file from stdin JSON,
//                                                     check its project, report problems on stderr
//   heal --serve                                      keep projects loaded so checks take well under a second
//   heal --server-status                              say whether the server is running (exit 0 if it is)
//   heal --stop                                       agent Stop hook: check every core with a changed file (git),
//                                                     so edits made through a shell are caught before the agent finishes
// Exit codes: 0 clean, 1 problems remain, 2 couldn't run. In --hook mode problems exit 2,
// which is how Claude Code hooks hand stderr back to the agent.
//
// --check and --hook ask a running check server first, and fall back to checking here. --hook also starts the
// server in the background for next time. FUNCTIONALLINT_NO_SERVER=1 turns the server off.
//
// A FUNCTIONALLINT_POLICY that names a missing file stops everything with exit 2: the gate fails closed rather
// than run without the owner's rules.

var cwd = Environment.CurrentDirectory;

try
{
    return await MainAsync();
}
catch (PolicyNotFoundException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

async Task<int> MainAsync()
{
    if (args.Contains("--serve"))
    {
        Policy.EnsureNamedExists();   // a policy named but missing: don't serve
        return await Server.RunAsync();
    }

    if (args.Contains("--server-status"))
    {
        var (running, socket) = await Server.StatusAsync();
        Console.WriteLine(running ? $"running ({socket})" : Server.Disabled ? "off (FUNCTIONALLINT_NO_SERVER)" : "not running");
        return running ? 0 : 1;
    }

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

    if (args.Contains("--stop"))
    {
        // Claude Code's Stop hook: before the agent finishes, check every core with a changed file. This catches
        // what the edit hook can't see, such as files changed by a shell command.
        var (folder, again) = Hook.StopInput(await Console.In.ReadToEndAsync(), cwd);
        var projects = Hook.ChangedFiles(folder).Select(Hook.ProjectFor).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var reports = new List<string>();
        foreach (var project in projects)
        {
            var (code, report) = await CheckAsync(project, focus: null, startServer: true);
            if (code == 1) reports.Add($"{Path.GetFileNameWithoutExtension(project)}:\n{report}");
        }
        if (reports.Count == 0) return 0;
        Console.Error.Write("FunctionalLint: before finishing, fix these problems in the cores you changed (changes made in the shell included):\n"
            + string.Join("\n", reports));
        // Stop the agent once (exit 2 hands this to it). If it's already been stopped once, report without stopping
        // it again (exit 1 shows the user), so an agent that can't fix something doesn't loop.
        return again ? 1 : 2;
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
}

async Task<(int Code, string Report)> CheckAsync(string target, string? focus, bool startServer)
{
    // Solutions are checked here: the server keeps single projects, which is what hooks ask for.
    if (!target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        return await CheckHereAsync(target, focus);

    // The server may run in another folder, so send absolute paths. And the policy this check must use: the server
    // only answers for the same one.
    Policy.EnsureNamedExists();   // a policy named but missing fails here, before any server is asked
    var request = new Server.Request(Path.GetFullPath(target), focus is null ? null : Path.GetFullPath(focus), cwd, Policy.Named());
    if (await Server.TryAskAsync(request, TimeSpan.FromSeconds(60)) is { } answer)
        return (answer.Code, answer.Report);

    var result = await CheckHereAsync(target, focus);
    if (startServer) await Server.StartInBackgroundAsync();
    return result;
}

async Task<(int Code, string Report)> CheckHereAsync(string target, string? focus)
{
    var clock = Stopwatch.StartNew();
    var loaded = await Checker.LoadWithDependentsAsync(target);
    Timing.Mark("load", Stopwatch.StartNew(), clock.Elapsed);
    using (loaded.Workspace)
        return await Checker.CheckAsync(loaded.Workspace.CurrentSolution, loaded.Functional, focus, cwd, clock);
}

async Task<(int Code, string Report)> HealAsync(string target)
{
    var clock = Stopwatch.StartNew();
    var loaded = await Checker.LoadWithDependentsAsync(target);
    using (loaded.Workspace)
        return await Checker.HealAsync(loaded, cwd, clock);
}
