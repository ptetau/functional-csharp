using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0016_ChecksSwitchedOff
{
    [Fact]
    public Task Pragmas_suppressions_and_nullable_disable_are_flagged() => Verify("""
        using System.Diagnostics.CodeAnalysis;

        public static class C
        {
        {|FP0016:#pragma warning disable FP0001|}
            public static int A() => 1;
        #pragma warning restore FP0001

            [{|FP0016:SuppressMessage("Functional", "FP0002:No mutation")|}]
            public static int B() => 2;

        {|FP0016:#nullable disable|}
            public static string D() => "";
        #nullable restore
        }
        """);

    [Fact]
    public Task Rules_the_owner_allows_may_be_suppressed_but_a_blanket_pragma_never() => Verify("""
        public static class C
        {
        #pragma warning disable CS0168
        {|FP0016:#pragma warning disable|}
            public static int A() => 1;
        }
        """, config: "functional_lint.may_lower = CS0168");
}
