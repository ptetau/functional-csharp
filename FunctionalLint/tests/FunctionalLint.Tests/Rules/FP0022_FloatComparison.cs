using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0022_FloatComparison
{
    [Fact]
    public Task Comparing_computed_floats_exactly_is_flagged() => Verify("""
        public static class C
        {
            public static bool Same(double a, double b) => {|FP0022:a == b|};
            public static bool Differs(float a, float b) => {|FP0022:a != b|};
            public static bool Sum(double a) => {|FP0022:a + 0.1 == a * 2|};
            public static bool Missing(double a) => {|FP0022:a == double.NaN|};
        }
        """);

    [Fact]
    public Task Constants_tolerances_and_other_types_are_fine() => Verify("""
        public static class C
        {
            public static bool Zero(double a) => a == 0;
            public static bool One(float a) => a != 1f;
            public static bool Close(double a, double b) => System.Math.Abs(a - b) < 1e-9;
            public static bool Ints(int a, int b) => a == b;
            public static bool Money(decimal a, decimal b) => a == b;
            public static bool Ordered(double a, double b) => a < b;
            public static bool Nan(double a) => double.IsNaN(a);
        }
        """);
}
