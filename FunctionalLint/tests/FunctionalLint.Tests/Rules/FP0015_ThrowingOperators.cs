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
}
