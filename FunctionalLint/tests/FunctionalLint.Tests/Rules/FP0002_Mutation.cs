using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0002_Mutation
{
    [Fact]
    public Task Reassignment_and_compound_assignment_are_flagged() => Verify("""
        static class C
        {
            static int M(int[] xs, string? s)
            {
                var x = 1;
                {|FP0002:x = 2|};
                {|FP0002:x += 1|};
                {|FP0002:x -= 1|};
                {|FP0002:s ??= ""|};
                {|FP0002:xs[0] = x|};
                return x + s.Length;
            }
        }
        """);

    [Fact]
    public Task Increment_and_decrement_are_flagged() => Verify("""
        static class C
        {
            static int M(int x)
            {
                {|FP0002:x++|};
                {|FP0002:--x|};
                return x;
            }
        }
        """);

    [Fact]
    public Task Declaring_a_local_then_assigning_it_is_flagged() => Verify("""
        static class C
        {
            static int M()
            {
                int x;
                {|FP0002:x = 1|};
                return x;
            }
        }
        """);

    [Fact]
    public Task Writes_through_ref_locals_and_properties_are_flagged() => Verify("""
        static class C
        {
            static void M(int[] xs, System.Text.StringBuilder {|FP0008:sb|})
            {
                ref int first = ref {|FP0015:xs[0]|};
                {|FP0002:first = 2|};
                {|FP0002:sb.Capacity = 10|};
            }
        }
        """);

    [Fact]
    public Task Subscribing_to_events_is_flagged() => Verify("""
        using System;

        public record R
        {
            public event Action? Changed;
            public void Watch() => {|FP0002:Changed += () => { }|};
        }
        """);

    [Fact]
    public Task Constructors_may_not_change_what_they_were_given() => Verify("""
        public record Pair
        {
            public Pair(int[] other) { {|FP0002:other[0] = 1|}; }
        }
        """);

    [Fact]
    public Task Building_new_values_is_allowed() => Verify("""
        public record Point(int X, int Y);
        public sealed record Box { public int W { get; init; } }

        public static class F
        {
            public static Point Move(Point p) => p with { X = p.X + 1 };
            public static Box Make() => new Box { W = 3 };
            public static object Anon() => new { A = 1 };
            public static int Sum((int, int) t)
            {
                var (a, b) = t;
                (var c, var d) = t;
                _ = c;
                return a + b + d;
            }
        }
        """);

    [Fact]
    public Task Constructors_init_accessors_and_static_constructors_may_set_their_own_members() => Verify("""
        public record Money
        {
            private readonly decimal _amount;
            private static readonly string Currency;

            static Money() { Currency = "NZD"; }
            public Money(decimal amount) { _amount = amount; this._amount = amount; }
            public decimal Amount { get => _amount; init => _amount = value; }
        }
        """);

    [Fact]
    public Task Writing_an_out_parameter_is_allowed() => Verify("""
        static class Parse
        {
            public static bool TryPositive(string s, out int value)
            {
                value = s.Length;
                return value > 0;
            }

            public static int Use(string s) => TryPositive(s, out var n) ? n : 0;
        }
        """);

    [Fact]
    public Task Compound_assignment_to_an_out_parameter_is_still_flagged() => Verify("""
        static class C
        {
            static void M(out int value)
            {
                value = 0;
                {|FP0002:value += 1|};
            }
        }
        """);
}

public class FP0002_MutationThroughCalls
{
    [Fact]
    public Task Out_and_ref_to_existing_variables_are_flagged() => Verify("""
        static class C
        {
            static int M(string s)
            {
                var n = 0;
                int.TryParse(s, {|FP0002:out n|});
                System.Threading.Interlocked.Increment({|FP0002:ref n|});
                return int.TryParse(s, out var fresh) ? fresh + n : n;
            }

            static bool Forward(string s, out int value) => int.TryParse(s, out value);

            static string Format(int n) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{n}:00");
        }
        """);

    [Fact]
    public Task Library_calls_that_change_their_argument_are_flagged() => Verify("""
        using System.Collections.Immutable;
        using System.Runtime.InteropServices;

        static class C
        {
            static ImmutableArray<int> M(int[] raw)
            {
                {|FP0002:System.Array.Fill(raw, 7)|};
                {|FP0002:System.Array.Sort(raw)|};
                return {|FP0002:ImmutableCollectionsMarshal.AsImmutableArray(raw)|};
            }
        }
        """);

    [Fact]
    public Task Writes_into_existing_arrays_and_buffers_are_flagged() => Verify("""
        using System;
        using System.Collections.Immutable;

        static class C
        {
            static int[] M(int[] src, int[] dst, byte[] bytes, ImmutableArray<int> items, string s, char[] chars)
            {
                {|FP0002:src.CopyTo(dst, 0)|};
                {|FP0002:dst.SetValue(42, 0)|};
                {|FP0002:Buffer.BlockCopy(src, 0, dst, 0, 4)|};
                {|FP0002:Buffer.SetByte(bytes, 0, 1)|};
                {|FP0002:items.CopyTo(dst)|};
                {|FP0002:s.CopyTo(0, chars, 0, 1)|};
                {|FP0002:s.AsSpan().CopyTo(chars)|};
                return dst;
            }
        }
        """);

    [Fact]
    public Task Reading_arrays_and_copying_into_new_ones_is_allowed() => Verify("""
        using System;
        using System.Linq;

        public sealed record Snapshot(int Size)
        {
            public Snapshot CopyTo(int[] target) => this with { Size = target.Length };
        }

        static class C
        {
            static object? A(int[] xs) => xs.GetValue(0);
            static int[] B(int[] xs) => xs.ToArray();
            static int[] D(int[] xs) => xs.AsSpan(1).ToArray();
            static int E(byte[] bytes) => Buffer.ByteLength(bytes) + Buffer.GetByte(bytes, 0);
            static Snapshot F(Snapshot s, int[] xs) => s.CopyTo(xs);
        }
        """);
}
