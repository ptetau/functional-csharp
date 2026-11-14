using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0020_ApiSubset
{
    [Fact]
    public Task Parts_of_dotnet_outside_the_subset_are_flagged() => Verify("""
        using System.Collections.Immutable;
        using System.Linq;
        using System.Text.RegularExpressions;

        public static class C
        {
            public static int Fine(ImmutableArray<int> xs) => xs.Where(static x => x > 0).Sum() + System.Math.Abs(-1) + "a".Length;
            public static bool Matches(string s) => Regex.IsMatch(s, "^a+$");

            public static int Hash(string s) => {|FP0020:s.GetHashCode()|};
            public static void Collect() => {|FP0020:System.GC.Collect()|};
            public static object Run() => {|FP0020:System.Threading.Tasks.Task.Run(static () => 1)|};
            public static object Error() => {|FP0020:new System.InvalidOperationException("x")|};
            public static int Step(System.Collections.Generic.IEnumerator<int> e) => {|FP0020:e.Current|};
        }
        """);

    [Fact]
    public Task A_contract_may_be_implemented() => Verify("""
        public sealed record Name(string Value)
        {
            public bool Equals(Name? other) => other is not null && Value == other.Value;
            public override int GetHashCode() => System.HashCode.Combine(Value.GetHashCode());
        }
        """);

    [Fact]
    public Task The_policy_can_allow_a_namespace_a_type_or_a_member() => Verify("""
        public static class C
        {
            public static void Collect() => System.GC.Collect();
            public static object Run() => System.Threading.Tasks.Task.Run(static () => 1);
            public static object Error() => {|FP0020:new System.InvalidOperationException("x")|};
        }
        """, config: "functional_lint.allowed_apis = System.GC, System.Threading.Tasks.Task.Run");
}
