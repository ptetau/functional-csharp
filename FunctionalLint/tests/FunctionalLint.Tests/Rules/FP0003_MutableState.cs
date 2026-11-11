using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0003_MutableState
{
    [Fact]
    public Task Non_readonly_fields_are_flagged() => Verify("""
        public record R
        {
            private int {|FP0003:_count|};
            private static int {|FP0003:_total|};
            public int {|FP0003:Open|};
        }
        """);

    [Fact]
    public Task Setters_are_flagged_including_private_and_indexer_setters() => Verify("""
        public record R
        {
            public int {|FP0003:A|} { get; set; }
            public int {|FP0003:B|} { get; private set; }
            public int {|FP0003:this|}[int i] { get => i; set { } }
        }
        """);

    [Fact]
    public Task Mutable_record_structs_are_reported_once_not_once_per_property() => Verify("""
        public record struct {|FP0003:Point|}(int X);
        """);

    [Fact]
    public Task Mutable_fields_in_structs_are_flagged() => Verify("""
        public struct S
        {
            public int {|FP0003:X|};
        }
        """);

    [Fact]
    public Task Readonly_const_init_and_get_only_are_allowed() => Verify("""
        public record R(int Positional)
        {
            private readonly int _a = 1;
            private const int B = 2;
            private static readonly int C = 3;
            public int D { get; init; }
            public int E { get; }
            public int F => _a + B + C;
        }

        public readonly record struct P(int X);
        public readonly struct Q { public readonly int X; public Q(int x) { X = x; } }
        """);
}
