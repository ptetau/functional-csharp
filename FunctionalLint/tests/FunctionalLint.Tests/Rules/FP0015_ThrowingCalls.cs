using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0015_ThrowingCalls
{
    [Fact]
    public Task Common_calls_that_throw_are_flagged() => Verify("""
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;

        static class C
        {
            static void M(ImmutableArray<int> xs, IReadOnlyDictionary<string, int> prices, ImmutableDictionary<string, int> map, string s)
            {
                var a = {|FP0015:xs.First()|};
                var b = {|FP0015:xs.Single(x => x > 1)|};
                var c = {|FP0015:xs.Max()|};
                var d = {|FP0015:xs.Aggregate((p, q) => p + q)|};
                var e = {|FP0015:prices["apple"]|};
                var f = {|FP0015:int.Parse(s)|};
                var g = {|FP0015:Enum.Parse<DayOfWeek>(s)|};
                var h = {|FP0015:Convert.ToInt32(s)|};
                var i = {|FP0015:xs.ToImmutableDictionary(x => x)|};
                var j = {|FP0015:map.Add("k", 1)|};
            }
        }
        """);

    [Fact]
    public Task The_safe_forms_are_allowed() => Verify("""
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;

        static class C
        {
            static void M(ImmutableArray<int> xs, ImmutableArray<string> names, IReadOnlyDictionary<string, int> prices, string s)
            {
                var a = xs.FirstOrDefault();
                var b = xs is [var only] ? only : 0;
                var c = xs.DefaultIfEmpty(0).Max();
                var d = xs.Aggregate(0, (p, q) => p + q);
                var e = prices.GetValueOrDefault("apple");
                var f = int.TryParse(s, out var n) ? n : 0;
                var g = xs.GroupBy(x => x).ToImmutableDictionary(group => group.Key, group => group.Count());
                var h = names.Max();
                var i = xs.Select(x => (int?)x).Max();
            }
        }
        """);

    [Fact]
    public Task Adapters_may_call_them() => Verify($$"""
        {{AdapterAttribute}}

        [ImperativeAdapter]
        static class Edge
        {
            static int M(string s) => int.Parse(s);
        }
        """);
}
