using System.Threading.Tasks;
using FunctionalLint.CodeFixes;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Fixes;

public class MutableStateFixTests
{
    [Fact]
    public Task Fields_become_readonly() => VerifyFix<MutableStateFix>("""
        public record R
        {
            private int {|FP0003:_a|} = 1;
            private static int {|FP0003:_b|} = 2;
            int {|FP0003:_c|} = 3;
            private volatile int {|FP0003:_d|} = 4;
        }
        """, """
        public record R
        {
            private readonly int _a = 1;
            private static readonly int _b = 2;
            readonly int _c = 3;
            private readonly int _d = 4;
        }
        """);

    [Fact]
    public Task Setters_become_init() => VerifyFix<MutableStateFix>("""
        public record R
        {
            private int {|FP0003:_z|};
            public int {|FP0003:A|} { get; set; }
            public int {|FP0003:B|} { get; private set; }
            public int {|FP0003:Z|} { get => _z; set => {|FP0002:_z = value|}; }
        }
        """, """
        public record R
        {
            private readonly int _z;
            public int A { get; init; }
            public int B { get; private init; }
            public int Z { get => _z; init => _z = value; }
        }
        """);

    [Fact]
    public Task Static_auto_property_setters_are_removed() => VerifyFix<MutableStateFix>("""
        public static class Settings
        {
            public static int {|FP0003:Retries|} { get; set; } = 3;
        }
        """, """
        public static class Settings
        {
            public static int Retries { get; } = 3;
        }
        """);

    [Fact]
    public Task Record_structs_become_readonly() => VerifyFix<MutableStateFix>("""
        public record struct {|FP0003:Point|}(int X, int Y);
        record struct {|FP0003:Empty|};
        """, """
        public readonly record struct Point(int X, int Y);
        readonly record struct Empty;
        """);
}

public class MutableStateFixSafetyTests
{
    [Fact]
    public Task Fields_that_could_never_be_set_are_left_alone() => Lint.VerifyFix<MutableStateFix>("""
        public struct Accumulator
        {
            public double {|FP0003:R|};
        }
        """, """
        public struct Accumulator
        {
            public double {|FP0003:R|};
        }
        """);

    // `readonly` on a mutable struct makes every call run on a copy: MoveNext() would never move.
    [Fact]
    public Task Fields_of_mutable_struct_types_are_left_alone()
    {
        const string source = """
            using System.Collections.Immutable;

            public struct Counter { public int {|FP0003:N|}; public void Bump() => {|FP0002:N++|}; }

            public sealed record Walker
            {
                private ImmutableArray<int>.Enumerator {|FP0003:_items|} = ImmutableArray.Create(1, 2).GetEnumerator();
                private Counter {|FP0003:_counter|} = new();
                private (Counter, int) {|FP0003:_pair|} = default;
                private Counter? {|FP0003:_maybe|} = null;
                public bool Next() => _items.MoveNext();
            }

            public sealed record Box<T>
            {
                private T {|FP0003:_value|} = default!;
                public T Value => _value;
            }
            """;
        return Lint.VerifyFix<MutableStateFix>(source, source);
    }

    [Fact]
    public Task Fields_of_types_that_copy_safely_become_readonly() => Lint.VerifyFix<MutableStateFix>("""
        using System;
        using System.Collections.Immutable;

        public enum Mode { On, Off }
        public struct Point { public readonly int X; public Point(int x) { X = x; } }

        public sealed record Holder<TRef> where TRef : class
        {
            private static ImmutableArray<int> {|FP0003:_a|} = ImmutableArray<int>.Empty;
            private DateTime {|FP0003:_b|} = default;
            private Mode {|FP0003:_c|} = Mode.On;
            private int? {|FP0003:_d|} = null;
            private (int, string) {|FP0003:_e|} = (1, "");
            private Point {|FP0003:_f|} = new(1);
            private TRef? {|FP0003:_g|} = null;
        }
        """, """
        using System;
        using System.Collections.Immutable;

        public enum Mode { On, Off }
        public struct Point { public readonly int X; public Point(int x) { X = x; } }

        public sealed record Holder<TRef> where TRef : class
        {
            private static readonly ImmutableArray<int> _a = ImmutableArray<int>.Empty;
            private readonly DateTime _b = default;
            private readonly Mode _c = Mode.On;
            private readonly int? _d = null;
            private readonly (int, string) _e = (1, "");
            private readonly Point _f = new(1);
            private readonly TRef? _g = null;
        }
        """);
}
