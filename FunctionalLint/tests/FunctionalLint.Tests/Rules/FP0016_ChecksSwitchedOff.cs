using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
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

    [Fact]
    public Task Suppressions_are_found_through_an_alias() => Verify("""
        using Quiet = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;
        using Hush = System.Diagnostics.CodeAnalysis;

        public static class C
        {
            [{|FP0016:Quiet("Functional", "FP0001")|}]
            public static int A() => 1;

            [{|FP0016:Hush::SuppressMessage("Functional", "FP0002")|}]
            public static int B() => 2;
        }
        """);

    [Fact]
    public Task Suppressions_are_found_through_a_global_alias_in_another_file() => VerifyFiles(
        "global using Quiet = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;",
        """
        public static class C
        {
            [{|FP0016:Quiet("Functional", "FP0001")|}]
            public static int A() => 1;
        }
        """);

    // Several files in one project; markup as in Verify.
    private static Task VerifyFiles(params string[] sources)
    {
        var test = new CSharpAnalyzerTest<FunctionalAnalyzer, DefaultVerifier> { ReferenceAssemblies = References };
        foreach (var source in sources) test.TestState.Sources.Add(source);
        test.TestBehaviors |= TestBehaviors.SkipSuppressionCheck;
        return test.RunAsync();
    }
}
