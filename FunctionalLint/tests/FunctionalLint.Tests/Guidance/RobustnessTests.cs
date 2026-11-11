using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalLint.Tests.Guidance;

/// <summary>
/// If the lint runs after every file write, it will often see half-written code, and it must be quick.
/// </summary>
public class RobustnessTests(ITestOutputHelper output)
{
    public static TheoryData<string> HalfWrittenCode => new()
    {
        "static class C { static void M() { for (var i = 0; i < ; i++) } }",
        "public class",
        "public record R(System.Collections.Generic.List<int> ",
        "static class C { static void M() { foreach (var x in ) { } } }",
        "static class C { static int M() { var x = 1; x = ; return x; } }",
        "static class C { static Missing M() => new Missing(); static object N() => Unknown.Now; }",
        "static class C { static System.Collections.Generic.List< M() => null; }",
        "[ImperativeAdapter] public",
        "static class C { static void M() { async void () => { }; } }",
        "public record struct P(int X",
        "namespace Legacy { public class A { } } static class C { static object M() => new Legacy.A(; }",
        "static class C { static void M(int[] xs) { xs[] = 1; throw; } }",
        "public class Box<T> where T : { }",
        "static class C { int x = 1; void M() { x++ } }",
    };

    [Theory]
    [MemberData(nameof(HalfWrittenCode))]
    public async Task Half_written_code_never_crashes_the_analyzer(string source)
    {
        var crashes = new ConcurrentBag<Exception>();
        var (_, analyzers) = await Lint.Prepare(source, "functional_lint.legacy_namespaces = Legacy\nfunctional_lint.impure_apis = Nope, A.B.",
            onCrash: crashes.Add);
        var diagnostics = await analyzers.GetAnalyzerDiagnosticsAsync();

        Assert.Empty(crashes);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    [Fact]
    public async Task A_large_clean_file_is_analysed_quickly()
    {
        // ~2,000 functional types and functions: bigger than any one file an agent should be writing.
        var source = new StringBuilder("using System.Collections.Immutable;\nusing System.Linq;\n");
        foreach (var i in Enumerable.Range(0, 500))
            source.AppendLine($$"""
                public sealed record Item{{i}}(int Id, string Name, int Weight);
                public static class Ops{{i}}
                {
                    public static Item{{i}} Rename(Item{{i}} item, string name) => item with { Name = name };
                    public static int Score(Item{{i}} item, ImmutableArray<int> tags) => tags.Where(t => t > 0).Sum() + item.Id * item.Weight;
                    public static ImmutableArray<Item{{i}}> Top(ImmutableArray<Item{{i}}> items) =>
                        items.OrderByDescending(item => Score(item, [item.Weight])).Take(10).ToImmutableArray();
                }
                """);

        var (compilation, analyzers) = await Lint.Prepare(source.ToString(), null, logTime: true);
        compilation.GetDiagnostics(); // Warm the compiler so we time only the analyzer.

        var clock = Stopwatch.StartNew();
        var diagnostics = await analyzers.GetAnalyzerDiagnosticsAsync();
        clock.Stop();
        var telemetry = await analyzers.GetAnalyzerTelemetryInfoAsync(analyzers.Analyzers[0], default);

        output.WriteLine($"{source.Length / 1024} KB, wall {clock.ElapsedMilliseconds} ms, analyzer {telemetry.ExecutionTime.TotalMilliseconds:0} ms");
        Assert.Empty(diagnostics);

        // Generous, so it only fails on a real regression (e.g. something accidentally quadratic).
        Assert.True(telemetry.ExecutionTime < TimeSpan.FromSeconds(5),
            $"Analyzer took {telemetry.ExecutionTime.TotalMilliseconds:0} ms on a {source.Length / 1024} KB file.");
    }
}
