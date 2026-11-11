using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0007_Throw
{
    [Fact]
    public Task Throw_statements_throw_expressions_and_rethrows_are_flagged() => Verify("""
        public static class C
        {
            public static int Length(string s) => s.Length > 0 ? s.Length : {|FP0007:throw|} new System.ArgumentException("empty");
            public static void Fail() { {|FP0007:throw|} new System.InvalidOperationException(); }
            public static void Again() { try { } catch { {|FP0007:throw|}; } }
        }
        """);

    [Fact]
    public Task Adapters_may_throw() => Verify($$"""
        {{AdapterAttribute}}

        [ImperativeAdapter]
        public static class Edge
        {
            public static void Fail() => throw new System.InvalidOperationException();
        }
        """);

    [Fact]
    public Task Catching_without_throwing_is_allowed() => Verify("""
        public static class C
        {
            public static int Safe(System.Func<int> f) { try { return f(); } catch { return 0; } }
        }
        """);
}
