using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0019_Subset
{
    [Fact]
    public Task Features_outside_the_subset_are_flagged() => Verify("""
        using System.Threading.Tasks;

        public static class C
        {
            private static readonly string Gate = "gate";
            public static int Lock() { {|FP0019:lock|} (Gate) { return 1; } }
            public static int Try(System.Func<int> f) { {|FP0019:try|} { return f(); } catch { return 0; } }
            public static string Bang(string? s) => {|FP0019:s!|};
            public static int Checked(int a) => {|FP0019:checked(a * a)|};
            public static bool Out(string s, {|FP0019:out|} int n) { n = s.Length; return true; }
            public static int Dynamic({|FP0019:dynamic|} d) => d;
            public static {|FP0019:async|} Task<int> Async() => {|FP0019:await Task.FromResult(1)|};
            public static int Using() { {|FP0019:using|} var reader = new System.IO.StringReader("x"); return reader.Peek(); }
            public static int Stack() { System.Span<int> s = {|FP0019:stackalloc int[2]|}; return s.Length; }
        }

        public sealed record Changes { public {|FP0019:event|} System.Action? Changed; }
        """);

    [Fact]
    public Task Open_dispatch_and_hidden_types_are_flagged() => Verify("""
        public {|FP0019:abstract|} record Shape;
        public sealed record Circle(double R) : {|FP0019:Shape|};

        public record Animal;
        public sealed record Dog : {|FP0019:Animal|};

        public closed record Payment;
        public sealed record Card(string Last4) : Payment;

        public record Base
        {
            public {|FP0019:virtual|} string Name() => "base";
        }

        public static class C
        {
            public static string Describe({|FP0019:object|} value) => value.ToString() ?? "";
        }
        """);

    [Fact]
    public Task Contracts_keep_object_and_interfaces_keep_static_abstract() => Verify("""
        public interface IZero<T> where T : IZero<T> { static abstract T Zero { get; } }

        public sealed record Money(decimal Amount) : IZero<Money>
        {
            public static Money Zero => new(0m);
        }

        public readonly record struct Point(int X, int Y)
        {
            public override string ToString() => $"({X}, {Y})";
        }

        public sealed record Wrapper(int Value) : System.IComparable
        {
            public int CompareTo(object? other) => other is Wrapper w ? Value.CompareTo(w.Value) : 1;
        }
        """);

    [Fact]
    public Task The_subset_itself_is_fine() => Verify("""
        using System.Collections.Immutable;
        using System.Linq;

        public sealed record Line(string Sku, int Quantity, decimal Price);

        public static class Orders
        {
            public static decimal Total(ImmutableArray<Line> lines) => lines.Sum(static l => l.Quantity * l.Price);

            public static string Describe(object value) => value switch
            {
                Line { Quantity: > 10 } big => $"big order of {big.Sku}",
                Line line => line.Sku,
                int n when n < 0 => "negative",
                _ => "unknown",
            };

            public static ImmutableArray<int> Evens(int n) =>
                [.. from i in Enumerable.Range(0, n) where i % 2 == 0 orderby i descending select i];

            public static System.Collections.Generic.IEnumerable<int> Twice(int x) { yield return x; yield return x; }

            public static int Wrap(long x) => unchecked((int)x);
        }
        """);

    [Fact]
    public Task Adapters_overrides_and_interface_implementations_are_left_alone() => Verify(AdapterAttribute + """

        public interface ILookup { bool TryGet(string key, {|FP0019:out|} int value); }

        public sealed record Table : ILookup
        {
            public bool TryGet(string key, out int value) { value = key.Length; return true; }
        }

        [ImperativeAdapter]
        public static class Files
        {
            private static readonly string Gate = "gate";
            public static int Read() { lock (Gate) { return 1; } }
        }

        public static class Mixed
        {
            public static System.Func<int> Later() => [ImperativeAdapter] () => { try { return 1; } catch { return 0; } };
        }
        """);

    [Fact]
    public Task The_policy_can_allow_a_feature() => Verify("""
        public static class C
        {
            private static readonly string Gate = "gate";
            public static int Lock() { lock (Gate) { return 1; } }
            public static string Bang(string? s) => {|FP0019:s!|};
        }
        """, config: "functional_lint.allowed_syntax = lock");
}
