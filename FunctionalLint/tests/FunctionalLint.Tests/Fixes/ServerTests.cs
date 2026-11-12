using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FunctionalLint.Heal;
using Xunit;

namespace FunctionalLint.Tests.Fixes;

/// <summary>
/// The check server end to end, on a real project in a temp folder: it answers, notices an edited file,
/// and notices a new one. Slower than the other tests (it restores and loads a project), so it's one test.
/// </summary>
public class ServerTests
{
    [Fact]
    public async Task Keeps_a_project_loaded_and_sees_edits_and_new_files()
    {
        var folder = Directory.CreateTempSubdirectory("heal-server").FullName;
        var stop = new CancellationTokenSource();
        try
        {
            var props = Path.Combine(Repo.Root, "build", "FunctionalCore.props");
            File.WriteAllText(Path.Combine(folder, "Core.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
                  <Import Project="{props}" />
                  <Import Project="shared.props" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(folder, "shared.props"), "<Project />");
            var file = Path.Combine(folder, "Order.cs");
            File.WriteAllText(file, "namespace Core;\npublic sealed record Order(int Id);\n");
            // No MSBuild node reuse: a reused node would outlive the restore and keep this test's process tree busy.
            Run("dotnet", $"restore \"{Path.Combine(folder, "Core.csproj")}\" -nodeReuse:false --disable-build-servers -v q");

            // A socket file left by a server that was killed mustn't stop a new one starting.
            var socket = Server.SocketPath!;
            File.WriteAllText(socket, "");
            var server = Task.Run(() => Server.RunAsync(stop.Token));

            // Paths relative to the client's folder, which isn't the server's.
            var request = new Server.Request("Core.csproj", "Order.cs", folder);
            Server.Response? answer = null;
            await WaitForAsync(() => (answer = Server.TryAskAsync(request, TimeSpan.FromSeconds(120)).Result) is not null);
            var clean = answer!;
            Assert.Equal(0, clean.Code);

            File.WriteAllText(file, "namespace Core;\npublic sealed record Order { public int Id { get; set; } }\n");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(1));
            var clock = Stopwatch.StartNew();
            var edited = await AskAsync(request);
            Assert.Equal(1, edited.Code);
            Assert.Contains("FP0003", edited.Report);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"A warm check took {clock.Elapsed.TotalSeconds:0.0}s.");

            var added = Path.Combine(folder, "Totals.cs");
            File.WriteAllText(added, "namespace Core;\npublic static class Totals { public static int First(int[] xs) => System.Linq.Enumerable.First(xs); }\n");
            var withNewFile = await AskAsync(request with { Focus = added });
            Assert.Contains("FP0015", withNewFile.Report);

            // Config changes are seen too: switching FP0003 off clears it.
            File.WriteAllText(Path.Combine(folder, ".editorconfig"), "root = true\n[*.cs]\ndotnet_diagnostic.FP0003.severity = none\n");
            var configured = await AskAsync(request);
            Assert.DoesNotContain("FP0003", configured.Report);

            // ...including imported .props files: turning FP0015 off there clears it.
            Assert.Contains("FP0015", (await AskAsync(request with { Focus = added })).Report);
            File.WriteAllText(Path.Combine(folder, "shared.props"),
                "<Project><PropertyGroup><NoWarn>$(NoWarn);FP0015</NoWarn></PropertyGroup></Project>");
            var imported = await AskAsync(request with { Focus = added });
            Assert.DoesNotContain("FP0015", imported.Report);
        }
        finally
        {
            stop.Cancel();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Answers_only_for_the_clients_own_policy()
    {
        // A server started with FUNCTIONALLINT_POLICY pointing elsewhere (or nowhere) must not answer for a client
        // whose environment says otherwise: that client checks in-process, against its own policy.
        var stop = new CancellationTokenSource();
        var server = Task.Run(() => Server.RunAsync(stop.Token));
        try
        {
            await WaitForAsync(() => Server.StatusAsync().Result.Running);
            var request = new Server.Request("Core.csproj", null, Path.GetTempPath(), Policy: "/nowhere/functionallint.policy");

            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Unspecified);
            await socket.ConnectAsync(new System.Net.Sockets.UnixDomainSocketEndPoint(Server.SocketPath!));
            await using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);
            await System.Text.Json.JsonSerializer.SerializeAsync(stream, request);
            socket.Shutdown(System.Net.Sockets.SocketShutdown.Send);
            var response = await System.Text.Json.JsonSerializer.DeserializeAsync<Server.Response>(stream);
            Assert.Equal(new Server.Response(2, "", Restart: true), response);
            Assert.Null(await Server.TryAskAsync(request, TimeSpan.FromSeconds(10)));
        }
        finally
        {
            stop.Cancel();
            await server;
        }
    }

    private static async Task<Server.Response> AskAsync(Server.Request request) =>
        await Server.TryAskAsync(request, TimeSpan.FromSeconds(120)) ?? throw new Xunit.Sdk.XunitException("The server didn't answer.");

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("The server didn't start.");
            await Task.Delay(50);
        }
    }

    private static void Run(string program, string arguments)
    {
        // Output goes to a log file, not a pipe: a pipe could be held open by an MSBuild process that outlives this one.
        // `dotnet test` doesn't show a child's console, so the log is the only way to see why a restore failed.
        var log = Path.Combine(Path.GetTempPath(), $"functionallint-{Guid.NewGuid():N}.log");
        var start = new ProcessStartInfo(program, $"{arguments} -flp:logfile=\"{log}\";verbosity=normal") { UseShellExecute = false };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        try
        {
            using var process = Process.Start(start)!;
            Assert.True(process.WaitForExit(TimeSpan.FromMinutes(3)), $"{program} {arguments} didn't finish.");
            if (process.ExitCode != 0)
            {
                var lines = File.Exists(log) ? File.ReadAllLines(log) : ["(no log)"];
                var errors = lines.Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).ToArray();
                Assert.Fail($"{program} {arguments} exited {process.ExitCode}:\n" + string.Join("\n", errors.Length > 0 ? errors : lines.TakeLast(40)));
            }
        }
        finally
        {
            File.Delete(log);
        }
    }

    [Fact]
    public async Task Healing_one_project_checks_the_projects_that_depend_on_it()
    {
        // Core's setter would become init, which compiles in Core but breaks Host. Heal is given only Core.csproj,
        // so it has to find the solution to see Host.
        var folder = Directory.CreateTempSubdirectory("heal-solution").FullName;
        try
        {
            var props = Path.Combine(Repo.Root, "build", "FunctionalCore.props");
            Directory.CreateDirectory(Path.Combine(folder, "Core"));
            Directory.CreateDirectory(Path.Combine(folder, "Host"));
            File.WriteAllText(Path.Combine(folder, "Core", "Core.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
                  <Import Project="{props}" />
                </Project>
                """);
            var settings = Path.Combine(folder, "Core", "Settings.cs");
            File.WriteAllText(settings, "namespace Core;\npublic sealed record Settings { public int Retries { get; set; } }\n");
            File.WriteAllText(Path.Combine(folder, "Host", "Host.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Core/Core.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(folder, "Host", "Use.cs"), "public static class Use { public static void M(Core.Settings s) { s.Retries = 3; } }\n");
            File.WriteAllText(Path.Combine(folder, "App.slnx"), """
                <Solution>
                  <Project Path="Core/Core.csproj" />
                  <Project Path="Host/Host.csproj" />
                </Solution>
                """);
            Run("dotnet", $"restore \"{Path.Combine(folder, "App.slnx")}\" -nodeReuse:false --disable-build-servers -v q");

            var loaded = await Checker.LoadWithDependentsAsync(Path.Combine(folder, "Core", "Core.csproj"));
            using (loaded.Workspace)
            {
                // --check agrees with heal: the setter isn't offered as auto-fixable, because heal will refuse it.
                var (_, check) = await Checker.CheckAsync(loaded.Workspace.CurrentSolution, loaded.Functional, null, folder, Stopwatch.StartNew());
                Assert.Contains("FP0003", check);
                Assert.DoesNotContain("[auto-fixable]", check);

                var (_, report) = await Checker.HealAsync(loaded, folder, Stopwatch.StartNew());
                Assert.Contains("set;", File.ReadAllText(settings));
                Assert.Contains("FP0003", report);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_solution_that_only_mentions_a_similar_name_is_skipped()
    {
        // App.slnx lists App/MyCore.csproj, whose name contains "Core.csproj". Heal must still find and fix Core.
        var folder = Directory.CreateTempSubdirectory("heal-decoy").FullName;
        try
        {
            var props = Path.Combine(Repo.Root, "build", "FunctionalCore.props");
            Directory.CreateDirectory(Path.Combine(folder, "Core"));
            File.WriteAllText(Path.Combine(folder, "Core", "Core.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
                  <Import Project="{props}" />
                </Project>
                """);
            var settings = Path.Combine(folder, "Core", "Settings.cs");
            File.WriteAllText(settings, "namespace Core;\npublic sealed record Settings { public int Retries { get; set; } }\n");
            File.WriteAllText(Path.Combine(folder, "App.slnx"), """<Solution><Project Path="App/MyCore.csproj" /></Solution>""");
            Run("dotnet", $"restore \"{Path.Combine(folder, "Core", "Core.csproj")}\" -nodeReuse:false --disable-build-servers -v q");

            var loaded = await Checker.LoadWithDependentsAsync(Path.Combine(folder, "Core", "Core.csproj"));
            using (loaded.Workspace)
            {
                Assert.Contains("weren't checked", loaded.Note);
                await Checker.HealAsync(loaded, folder, Stopwatch.StartNew());
                Assert.Contains("init;", File.ReadAllText(settings));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
    [Fact]
    public async Task A_policy_core_that_stopped_loading_FunctionalLint_is_still_checked_and_reported()
    {
        // The project file lost its FunctionalCore.props import, which would silently switch every check off.
        var folder = Directory.CreateTempSubdirectory("heal-policy").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "functionallint.policy"), "is_global = true\nfunctional_lint.cores = Core/Core.csproj\n");
            Directory.CreateDirectory(Path.Combine(folder, "Core"));
            var project = Path.Combine(folder, "Core", "Core.csproj");
            File.WriteAllText(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(folder, "Core", "Prices.cs"),
                "namespace Core;\npublic static class Prices { public static int Total(int[] xs) { var t = 0; foreach (var x in xs) t += x; return t; } }\n");
            Run("dotnet", $"restore \"{project}\" -nodeReuse:false --disable-build-servers -v q");

            var loaded = await Checker.LoadAsync(project);
            using (loaded.Workspace)
            {
                var (code, report) = await Checker.CheckAsync(loaded.Workspace.CurrentSolution, loaded.Functional, null, folder, Stopwatch.StartNew());
                Assert.Equal(1, code);
                Assert.Contains("error FP0016: 'Core' is a policy core but doesn't load FunctionalLint. Restore its import of FunctionalCore.props.", report);
                Assert.Contains("FP0001", report);   // and every rule still runs
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
