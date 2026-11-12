using System;
using System.Diagnostics;
using System.IO;
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
                </Project>
                """);
            var file = Path.Combine(folder, "Order.cs");
            File.WriteAllText(file, "namespace Core;\npublic sealed record Order(int Id);\n");
            // No MSBuild node reuse: a reused node would outlive the restore and keep this test's process tree busy.
            Run("dotnet", $"restore \"{Path.Combine(folder, "Core.csproj")}\" -nodeReuse:false --disable-build-servers -v q");

            var server = Task.Run(() => Server.RunAsync(stop.Token));
            await WaitForAsync(() => File.Exists(Server.SocketPath));

            var request = new Server.Request(Path.Combine(folder, "Core.csproj"), file, folder);
            var clean = await AskAsync(request);
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
        }
        finally
        {
            stop.Cancel();
            Directory.Delete(folder, recursive: true);
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
        var start = new ProcessStartInfo(program, arguments) { UseShellExecute = false };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(3)), $"{program} {arguments} didn't finish.");
        Assert.Equal(0, process.ExitCode);
    }
}
