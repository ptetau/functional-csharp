using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0012_IgnoredResults
{
    [Fact]
    public Task Ignoring_the_result_of_an_immutable_collection_method_is_flagged() => Verify("""
        using System.Collections.Immutable;

        static class C
        {
            static ImmutableArray<int> M(ImmutableArray<int> xs, ImmutableDictionary<string, int> map)
            {
                {|FP0012:xs.Add(1)|};
                {|FP0012:map.SetItem("a", 1)|};
                {|FP0012:ImmutableList.Create(1, 2)|};
                return xs;
            }
        }
        """);

    [Fact]
    public Task Ignoring_LINQ_string_and_date_results_is_flagged() => Verify("""
        using System;
        using System.Linq;

        static class C
        {
            static void M(int[] xs, string s, DateTime when)
            {
                {|FP0012:xs.Select(x => x * 2)|};
                {|FP0012:s.Trim()|};
                {|FP0012:string.Join(",", xs)|};
                {|FP0012:when.AddDays(1)|};
            }
        }
        """);

    [Fact]
    public Task Using_or_explicitly_discarding_the_result_is_allowed() => Verify("""
        using System.Collections.Immutable;
        using System.Linq;

        static class C
        {
            static ImmutableArray<int> M(ImmutableArray<int> xs)
            {
                var more = xs.Add(1);
                _ = xs.Contains(2);
                return more.Where(x => x > 0).ToImmutableArray();
            }
        }
        """);

    [Fact]
    public Task Builders_interlocked_and_void_methods_are_allowed() => Verify("""
        using System.Collections.Immutable;

        static class C
        {
            static ImmutableArray<int> M(ImmutableDictionary<string, int> map)
            {
                var builder = ImmutableArray.CreateBuilder<int>();
                builder.Add(1);
                builder.Remove(1);
                ImmutableInterlocked.TryAdd(ref map, "a", 1);
                return builder.ToImmutable();
            }
        }
        """);

    [Fact]
    public Task Adapters_are_exempt() => Verify($$"""
        {{AdapterAttribute}}

        [ImperativeAdapter]
        static class C
        {
            static void M(string s) => s.Trim();
        }
        """);
}
