using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Guidance;

/// <summary>
/// The message is the only thing a developer or agent reliably sees: `dotnet build` prints it and nothing else.
/// So every message must name the offending code and say what to write instead, on one short line.
///
/// This file holds the exact text of every message the analyzer can produce. If you change a message,
/// change it here too; the diff then shows reviewers exactly what guidance changed.
/// </summary>
public class MessageTests
{
    private const string LegacyConfig = "functional_lint.legacy_namespaces = Legacy";

    // (rule, code that triggers it, config, exact message)
    public static TheoryData<string, string, string?, string> Messages => new()
    {
        { "FP0001", "static class C { static void M() { while (true) { } } }", null,
            "'while' loops are not allowed. Use LINQ, Aggregate or recursion instead." },
        { "FP0001", "static class C { static async System.Threading.Tasks.Task M(System.Collections.Generic.IAsyncEnumerable<int> xs) { await foreach (var x in xs) { } } }", null,
            "'foreach' loops are not allowed. Use LINQ, Aggregate or recursion instead." },
        { "FP0002", "static class C { static int M(int total) { total += 1; return total; } }", null,
            "'total' is changed here. Bind a new name or use a 'with' expression instead." },
        { "FP0003", "public record R { private int _count; }", null,
            "Field '_count' is mutable. Mark it readonly." },
        { "FP0003", "public record R { public int Count { get; set; } }", null,
            "Property 'Count' is mutable. Use 'init' instead of 'set', or make it get-only." },
        { "FP0003", "public record R { public int this[int i] { get => i; set { } } }", null,
            "Indexer 'this[]' is mutable. Use 'init' instead of 'set', or make it get-only." },
        { "FP0003", "public record struct P;", null,
            "Record struct 'P' is mutable. Declare it as 'readonly record struct'." },
        { "FP0004", "public class Account { }", null,
            "Class 'Account' is not allowed. Use a record for data or a static class for functions." },
        { "FP0005", "static class C { static object M() => new System.Collections.Generic.List<int>(); }", null,
            "'List<int>' is mutable. Use ImmutableArray<T> or ImmutableList<T> instead." },
        { "FP0005", "using System.Linq; static class C { static object M(int[] xs) => xs.ToList(); }", null,
            "'List<int>' is mutable. Use ToImmutableArray() or ToImmutableList() instead." },
        { "FP0005", "using System.Linq; static class C { static object M(int[] xs) => xs.ToDictionary(x => x); }", null,
            "'Dictionary<int, int>' is mutable. Use ToImmutableDictionary() instead." },
        { "FP0005", "static class C { static object M() => new System.Text.StringBuilder(); }", null,
            "'StringBuilder' is mutable. Use string interpolation or string.Join instead." },
        { "FP0006", "namespace Legacy { public static class Db { public static int Load() => 1; } } static class C { static int M() => Legacy.Db.Load(); }", LegacyConfig,
            "'Db' is legacy imperative code. Call it through an [ImperativeAdapter] instead." },
        { "FP0007", "static class C { static void M() { throw new System.Exception(); } }", null,
            "Don't throw here. Return an Error on the Result railway, or throw from an [ImperativeAdapter]." },
        { "FP0008", "static class C { static int M(System.Collections.Generic.IList<int> xs) => 0; }", null,
            "'IList<int>' lets callers change the collection. Use IReadOnlyList<T> or ImmutableArray<T> instead." },
        { "FP0008", "static class C { static System.Collections.Generic.Dictionary<string, int> M() => null!; }", null,
            "'Dictionary<string, int>' lets callers change the collection. Use ImmutableDictionary<TKey, TValue> instead." },
        { "FP0009", "namespace Legacy { public sealed record Db; } public sealed class ImperativeAdapterAttribute : System.Attribute { } [ImperativeAdapter] public static class A { public static Legacy.Db Get() => null!; }", LegacyConfig,
            "'Get' exposes legacy type 'Db'. Map it to a functional type (a record or Result) instead." },
        { "FP0010", "static class C { static async void Fire() => await System.Threading.Tasks.Task.Yield(); }", null,
            "'Fire' is async void, so its errors can't be observed. Return Task instead." },
        { "FP0010", "static class C { static System.Action M() => async () => await System.Threading.Tasks.Task.Yield(); }", null,
            "'lambda' is async void, so its errors can't be observed. Return Task instead." },
        { "FP0011", "static class C { static System.DateTime M() => System.DateTime.Now; }", null,
            "'DateTime.Now' has side effects. Call it from an [ImperativeAdapter], or pass the value in." },
        { "FP0011", "static class C { static System.Random M() => new System.Random(); }", null,
            "'Random' has side effects. Call it from an [ImperativeAdapter], or pass the value in." },
        { "FP0005", "static class C { static int M() { System.Collections.Generic.IList<int> xs = [1]; return xs.Count; } }", null,
            "'IList<int>' is mutable. Use IReadOnlyList<T> or ImmutableArray<T> instead." },
        { "FP0012", "using System.Collections.Immutable; static class C { static void M(ImmutableArray<int> xs) { xs.Add(1); } }", null,
            "'ImmutableArray.Add' returns a new value and leaves the original unchanged. Use the result instead." },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public async Task Each_message_reads_exactly_as_expected(string id, string source, string? config, string expected)
    {
        var finding = Assert.Single(await Diagnose(source, config));
        Assert.Equal(id, finding.Id);
        Assert.Equal(expected, finding.Message);
    }

    // Just (rule, message), for checks that only look at the text.
    public static TheoryData<string, string> Texts()
    {
        var data = new TheoryData<string, string>();
        foreach (var row in Messages) data.Add((string)row[0], (string)row[3]);
        return data;
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void Each_message_is_one_short_line(string id, string message)
    {
        Assert.DoesNotContain('\n', message);
        Assert.True(message.Length <= 120, $"{id} message is {message.Length} chars; keep it under 120 so it fits on one line of build output.");
        Assert.DoesNotMatch(new Regex(@"\{\d+\}"), message);
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void Each_message_says_what_is_wrong_then_what_to_do(string id, string message)
    {
        // Two sentences at least: the problem, then the remedy.
        var sentences = Regex.Split(message.TrimEnd('.'), @"(?<=\.)\s+(?=[A-Z'])");
        Assert.True(sentences.Length >= 2, $"{id}: '{message}' should state the problem and then the remedy.");

        // The remedy is an instruction, so it starts with a verb.
        string[] verbs = ["Use", "Bind", "Mark", "Declare", "Call", "Return", "Map"];
        var remedy = sentences.Last();
        Assert.True(verbs.Any(v => remedy.StartsWith(v)),
            $"{id}: remedy '{remedy}' should start with one of: {string.Join(", ", verbs)}.");
    }

    [Fact]
    public void Every_rule_has_at_least_one_message_example()
    {
        var covered = Messages.Select(row => (string)row[0]).ToHashSet();
        var missing = new FunctionalAnalyzer().SupportedDiagnostics.Select(d => d.Id).Where(id => !covered.Contains(id));
        Assert.Empty(missing);
    }

    [Fact]
    public async Task Diagnostics_point_at_the_smallest_useful_span()
    {
        // A loop is reported on its keyword and a type on its name, so the whole body isn't underlined.
        var findings = await Diagnose("""
            public class Account
            {
                public void M() { foreach (var x in new int[0]) { var y = x; } }
            }
            """);
        Assert.Equal(["Account", "foreach"], findings.Select(f => f.Code));
    }
}
