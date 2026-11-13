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

    // A project with its own [ValueEquality] types, like samples/Functional: FP0014 suggests them by name.
    private const string ValueTypes =
        "public sealed class ValueEqualityAttribute : System.Attribute { } " +
        "[ValueEquality] public readonly record struct ValueList<T>; " +
        "[ValueEquality] public readonly record struct ValueMap<TKey, TValue>; ";

    // (rule, code that triggers it, config, exact message)
    public static TheoryData<string, string, string?, string> Messages => new()
    {
        { "FP0001", "static class C { static void M() { while (true) { } } }", null,
            "'while' loops are not allowed. Use LINQ, Aggregate or recursion instead." },
        { "FP0001", "static class C { static async System.Threading.Tasks.Task M(System.Collections.Generic.IAsyncEnumerable<int> xs) { await foreach (var x in xs) { } } }", null,
            "'foreach' loops are not allowed. Use LINQ, Aggregate or recursion instead." },
        { "FP0002", "static class C { static int M(int total) { total += 1; return total; } }", null,
            "'total' is changed here. Bind a new name or use a 'with' expression instead." },
        { "FP0002", "static class C { static void M(int[] xs) { xs[0] = 1; } }", null,
            "'xs[0]' is changed here. Build a new array with Select(...).ToArray() instead." },
        { "FP0002", "static class C { static int M(string s) { var n = 0; int.TryParse(s, out n); return n; } }", null,
            "'n' is changed here. Use 'out var' to bind a new name instead." },
        { "FP0002", "static class C { static void M(int[] xs) { System.Array.Fill(xs, 0); } }", null,
            "'Array.Fill' changes its argument in place. Build a new value with LINQ instead." },
        { "FP0002", "static class C { static void M(int[] xs) { xs.SetValue(1, 0); } }", null,
            "'Array.SetValue' changes the array it's called on. Build a new array with Select(...).ToArray() instead." },
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
        { "FP0005", "using System.Linq; static class C { static object M(int[] xs) => xs.ToHashSet(); }", null,
            "'HashSet<int>' is mutable. Use ToImmutableHashSet() instead." },
        { "FP0005", "static class C { static object M() => new System.Text.StringBuilder(); }", null,
            "'StringBuilder' is mutable. Use string interpolation, string.Join or string.Concat instead." },
        { "FP0005", "static class C { static object M() => System.Collections.Immutable.ImmutableArray.CreateBuilder<int>(); }", null,
            "'ImmutableArray<int>.Builder' is mutable. Use Select(...).ToImmutableArray() instead." },
        { "FP0006", "namespace Legacy { public static class Db { public static int Load() => 1; } } static class C { static int M() => Legacy.Db.Load(); }", LegacyConfig,
            "'Db' is legacy imperative code. Call it through an [ImperativeAdapter] instead." },
        { "FP0007", "static class C { static void M() { throw new System.Exception(); } }", null,
            "Don't throw here. Return an Error on the Result railway, or throw from an [ImperativeAdapter]." },
        { "FP0008", "static class C { static int M(System.Collections.Generic.IList<int> xs) => 0; }", null,
            "'IList<int>' lets callers change the collection. Use IReadOnlyList<T> or ImmutableArray<T> instead." },
        { "FP0008", "static class C { static System.Collections.Generic.Dictionary<string, int> M() => null!; }", null,
            "'Dictionary<string, int>' lets callers change the collection. Use IReadOnlyDictionary<TKey, TValue> instead." },
        { "FP0008", "static class C { static readonly System.Collections.Generic.IList<int> Cache = null!; }", null,
            "'IList<int>' in a field can be changed by any code that can see it. Use IReadOnlyList<T> or ImmutableArray<T> instead." },
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
        { "FP0011", "static class C { static string M() => System.Environment.NewLine; }", null,
            "'Environment.NewLine' depends on the machine it runs on. Call it from an [ImperativeAdapter], or pass the value in." },
        { "FP0011", "static class C { static object? M(System.Reflection.PropertyInfo p) => p.GetValue(null); }", null,
            "'PropertyInfo.GetValue' reaches members chosen at run time. Call it from an [ImperativeAdapter], or pass the value in." },
        { "FP0011", "static class C { static string M(string s) => s.ReplaceLineEndings(); }", null,
            "'string.ReplaceLineEndings()' uses Environment.NewLine, which is \"\\r\\n\" on Windows. Use ReplaceLineEndings(\"\\n\") instead." },
        { "FP0011", "static class C { static readonly System.Xml.XmlWriterSettings O = new() { Indent = true }; }", null,
            "'XmlWriterSettings.Indent' writes Environment.NewLine, which is \"\\r\\n\" on Windows. Use NewLineChars = \"\\n\" in the same initializer." },
        { "FP0011", "static class C { static readonly System.Text.Json.JsonSerializerOptions O = new() { WriteIndented = true }; }", null,
            "'JsonSerializerOptions.WriteIndented' writes Environment.NewLine, which is \"\\r\\n\" on Windows. Use NewLine = \"\\n\" in the same initializer." },
        { "FP0014", "public sealed record Order(string Id, System.Collections.Immutable.ImmutableArray<string> Tags);", null,
            "'ImmutableArray<string>' compares by reference, so equal 'Order' records won't be equal. Use a [ValueEquality] collection instead." },
        { "FP0014", ValueTypes + "public sealed record Order(string Id, System.Collections.Immutable.ImmutableArray<string> Tags);", null,
            "'ImmutableArray<string>' compares by reference, so equal 'Order' records won't be equal. Use ValueList<T> instead." },
        { "FP0014", ValueTypes + "public sealed record Prices(System.Collections.Immutable.ImmutableDictionary<string, decimal> BySku);", null,
            "'ImmutableDictionary<string, decimal>' compares by reference, so equal 'Prices' records won't be equal. Use ValueMap<TKey, TValue> instead." },
        { "FP0015", "using System.Linq; static class C { static int M(int[] xs) => xs.First(); }", null,
            "'First' throws when nothing matches. Use FirstOrDefault, or a list pattern such as 'is [var first, ..]' instead." },
        { "FP0015", "static class C { static int M(string s) => int.Parse(s); }", null,
            "'int.Parse' throws when the text isn't valid. Use int.TryParse instead." },
        { "FP0015", "static class C { static int M(System.Collections.Generic.IReadOnlyDictionary<string, int> d) => d[\"a\"]; }", null,
            "'d[\"a\"]' throws when the key is missing. Use TryGetValue or GetValueOrDefault instead." },
        { "FP0016", "#pragma warning disable CS0168\nstatic class C { }", null,
            "'#pragma warning disable CS0168' turns checks off. Remove it and fix the code it hides." },
        { "FP0017", Lint.AdapterAttribute + " namespace App.Core { [ImperativeAdapter] static class Clock { } }", "functional_lint.adapter_namespaces = App.Adapters",
            "'Clock' is an [ImperativeAdapter] outside 'App.Adapters'. Move it there, or pass the value in." },
        { "FP0020", "static class C { static void M() => System.GC.Collect(); }", null,
            "'GC.Collect' is outside the .NET subset a core may use. Move it to a shell project, or ask the owner to allow it." },
        { "FP0019", "static class C { static string M(string? s) => s!; }", null,
            "'!' is outside the subset: it hides a possible null. Handle the null: a pattern, ??, or an Option." },
        { "FP0019", "static class C { static bool M(string s, out int n) { n = s.Length; return true; } }", null,
            "'out' is outside the subset: it writes to the caller's variable. Return a tuple, an Option or a Result." },
        { "FP0019", "static class C { static readonly object Gate = new(); static int M() { lock (Gate) { return 1; } } }", null,
            "'lock' is outside the subset: a core has no shared state to guard. Move it to a shell project." },
        { "FP0001", "static class C { static void M(System.Collections.Immutable.ImmutableList<int> xs) => xs.ForEach(x => { }); }", null,
            "'ForEach' loops are not allowed. Use LINQ, Aggregate or recursion instead." },
        { "FP0005", "static class C { static int M() { System.Collections.Generic.IList<int> xs = [1]; return xs.Count; } }", null,
            "'IList<int>' is mutable. Use IReadOnlyList<T> or ImmutableArray<T> instead." },
        { "FP0012", "using System.Collections.Immutable; static class C { static void M(ImmutableArray<int> xs) { xs.Add(1); } }", null,
            "'ImmutableArray.Add' returns a new value and leaves the original unchanged. Use the result instead." },
        { "FP0013", "public enum Colour { Red, Green } static class C { static int M(Colour c) => c switch { Colour.Red => 1, _ => 0 }; }", null,
            "'_' matches every case of 'Colour' that isn't listed, so a new case won't be an error. List each case instead." },
        { "FP0013", "public enum Colour { Red, Green } static class C { static int M(Colour c) => (int)c switch { 0 => 1, _ => 0 }; }", null,
            "'(int)c' turns the enum 'Colour' into 'int', so a new case won't be an error. Use a switch on 'c' that lists each case instead." },
        { "FP0007", "static class C { static void M(string? s) => System.ArgumentNullException.ThrowIfNull(s); }", null,
            "Don't throw here. Return an Error on the Result railway, or throw from an [ImperativeAdapter]." },
        { "FP0013", "public enum Colour { Red, Green } static class C { static int M(Colour c) { switch (c) { default: return 0; } } }", null,
            "A switch statement over the enum 'Colour' isn't checked for missing cases. Use a switch expression instead." },
    };

    // Messages that need more than one source file to produce (FP0018 needs a second library) or that the gate
    // reports about a whole project: checked word for word in their own tests, and listed here for the style rules.
    public static TheoryData<string, string> ProjectLevel => new()
    {
        { "FP0018", "'Api.Query' comes from 'Acme.Db', which isn't on this core's list of allowed references. Move the code that uses it to a shell project." },
        { "FP0016", "'FP0001' is lowered to 'warning' in 'Ledger.Core' (NoWarn, .editorconfig or .globalconfig). Remove that setting." },
        { "FP0016", "'Ledger.Core' has nullable checking turned off. Remove the <Nullable> setting that turns it off." },
        { "FP0016", "'Text.cs' is skipped as generated code. Remove the marker (<auto-generated> header, .g.cs name or generated_code)." },
        { "FP0016", "'Ledger.Core' is a policy core but doesn't load FunctionalLint. Restore its import of FunctionalCore.props." },
        { "FP0016", "'[GeneratedCode(\"tool\", \"1.0\")]' makes every analyzer skip what it marks. Remove it and fix the code it hides." },
        { "FP0016", "'#line hidden' hides the code after it from every analyzer. Remove it and fix the code it hides." },
        { "FP0016", "'CS8509' isn't an error in 'Ledger.Core'. Restore TreatWarningsAsErrors and the WarningsAsErrors that FunctionalCore.props sets." },
        { "FP0016", "'RunAnalyzersDuringBuild' is 'false' for 'Ledger.Core', so its build runs no analyzers. Remove that setting." },
        { "FP0016", "'samples/Ledger/functionallint.policy' would replace the root policy for 'Ledger.Core' in the build. Remove it, and change the root policy instead." },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public async Task Each_message_reads_exactly_as_expected(string id, string source, string? config, string expected)
    {
        var finding = Assert.Single(await Diagnose(source, config, subset: id is "FP0019" or "FP0020"));
        Assert.Equal(id, finding.Id);
        Assert.Equal(expected, finding.Message);
    }

    // Just (rule, message), for checks that only look at the text.
    public static TheoryData<string, string> Texts()
    {
        var data = new TheoryData<string, string>();
        foreach (var row in Messages) data.Add((string)row[0], (string)row[3]);
        foreach (var row in ProjectLevel) data.Add((string)row[0], (string)row[1]);
        return data;
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void Each_message_is_one_short_line(string id, string message)
    {
        Assert.DoesNotContain('\n', message);
        // Type and member names vary, so measure the wording around them.
        var wording = Regex.Replace(message, "'[^']*'", "''").Length;
        Assert.True(wording <= 110, $"{id} message is {wording} chars without its quoted names; keep it short enough for one line of build output.");
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
        string[] verbs = ["Use", "Bind", "Mark", "Declare", "Call", "Return", "Map", "List", "Build", "Move", "Remove", "Restore", "Handle", "Take", "Rewrite"];
        var remedy = sentences.Last();
        Assert.True(verbs.Any(v => remedy.StartsWith(v)),
            $"{id}: remedy '{remedy}' should start with one of: {string.Join(", ", verbs)}.");
    }

    [Fact]
    public void Every_rule_has_at_least_one_message_example()
    {
        var covered = Messages.Select(row => (string)row[0]).Concat(ProjectLevel.Select(row => (string)row[0])).ToHashSet();
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
