using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0015_ThrowingOperators
{
    [Fact]
    public Task Casts_that_can_fail_are_flagged_and_safe_ones_are_not() => Verify("""
        public abstract record Shape;
        public sealed record Circle(double R) : Shape;
        public enum Colour { Red }

        public static class C
        {
            public static Circle Down(Shape s) => {|FP0015:(Circle)s|};
            public static int Unbox(object o) => {|FP0015:(int)o|};
            public static int Value(int? maybe) => {|FP0015:(int)maybe|};

            public static int Numeric(double d) => (int)d;
            public static Colour Enum(int i) => (Colour)i;
            public static Shape Up(Circle c) => (Shape)c;
            public static Circle? As(Shape s) => s as Circle;
            public static double Pattern(Shape s) => s is Circle c ? c.R : 0;
        }
        """);

    [Fact]
    public Task Integer_and_decimal_division_by_what_may_be_zero_is_flagged() => Verify("""
        public static class C
        {
            public static int Divide(int a, int b) => {|FP0015:a / b|};
            public static decimal Average(decimal total, int count) => {|FP0015:total / count|};
            public static int Rest(long a, int b) => (int)({|FP0015:a % b|});

            public static double Floats(double a, double b) => a / b;
            public static int Constant(int a) => a / 2;
            public static int Checked(int a, int b) => b != 0 ? a / b : 0;
            public static int Guarded(int a, int b) => b > 0 && a / b > 1 ? 1 : 0;
            public static int Pattern(int a, int b) => b is not 0 ? a % b : 0;
            public static int Arm(int a, int b) => b switch { 0 => 0, _ when b != 0 => a / b, _ => 0 };
            public static int AtLeastOne(int a, int count) { var n = System.Math.Max(1, count); return a / n; }
            public static int Clamped(int a, int count) => a / System.Math.Clamp(count, 1, 10);
        }
        """);

    [Fact]
    public Task Indexing_that_may_be_out_of_range_is_flagged() => Verify("""
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;

        public static class C
        {
            public static int First(int[] xs) => {|FP0015:xs[0]|};
            public static int Last(IReadOnlyList<int> xs) => {|FP0015:xs[^1]|};
            public static int At(ImmutableArray<int> xs, int i) => {|FP0015:xs[i]|};
            public static char Letter(string s) => {|FP0015:s[0]|};
            public static int Short(int[] xs) => xs.Length > 0 ? {|FP0015:xs[1]|} : 0;
            public static int Other(int[] xs, int[] ys) => ys.Length > 0 ? {|FP0015:xs[0]|} : 0;
        }
        """);

    [Fact]
    public Task Indexing_after_a_check_is_not_flagged() => Verify("""
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;

        public sealed record Box(ImmutableArray<int> {|FP0014:Items|});

        public static class C
        {
            public static int Checked(int[] xs, int i) => i < xs.Length ? xs[i] : 0;
            public static int Mirrored(int[] xs, int i) => xs.Length > i ? xs[i] : 0;
            public static int Second(int[] xs) => xs.Length > 1 ? xs[1] : 0;
            public static int Count(IReadOnlyList<int> xs) => xs.Count >= 2 ? xs[1] : 0;
            public static int Or(ImmutableArray<int> xs) => xs.Length == 0 || xs[0] > 3 ? 0 : 1;
            public static int Any(int[] xs) => xs.Any() ? xs[0] : 0;
            public static int Empty(ImmutableArray<int> xs) => xs.IsEmpty ? 0 : xs[^1];
            public static int List(int[] xs) => xs is [_, _, ..] ? xs[1] : 0;
            public static int Property(Box box) => box is { Items.Length: > 0 } b ? b.Items[0] : 0;
            public static int Arm(Box box) => box switch { { Items: [_, ..] } => box.Items[0], _ => 0 };
            public static int Early(int[] xs)
            {
                if (xs.Length < 3) return 0;
                return xs[2];
            }
            public static string Split(string s) => s.Split(',')[0];
            public static int Ranged(int[] xs) => Enumerable.Range(0, xs.Length).Sum(i => xs[i]);
            public static int Indexed(int[] xs) => xs.Select((x, i) => x + xs[i]).Sum();
            public static int[] Slice(int[] xs) => xs[1..];
        }
        """);

    [Fact]
    public Task Default_immutable_arrays_are_flagged() => Verify("""
        using System.Collections.Immutable;

        public static class C
        {
            public static ImmutableArray<int> Default() => {|FP0015:default(ImmutableArray<int>)|};
            public static ImmutableArray<int> New() => {|FP0015:new ImmutableArray<int>()|};
            public static ImmutableArray<int> Empty() => ImmutableArray<int>.Empty;
            public static ImmutableArray<int> Literal() => [];
        }
        """);
}
