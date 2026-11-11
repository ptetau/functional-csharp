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
        public record struct {|FP0003:Point|}(int {|FP0003:X|}, int {|FP0003:Y|});
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
}
