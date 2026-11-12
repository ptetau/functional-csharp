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
                var j = xs.GroupBy(x => x % 2).Select(g => g.First() + g.Max()).ToImmutableArray();
                var k = xs.Append(18).Max() + xs.Prepend(0).Aggregate((p, q) => p + q);
                var l = names.DistinctBy(n => n).ToImmutableDictionary(n => n, n => n.Length);
                var m = System.Text.RegularExpressions.Regex.Match(s, "(?<host>x)");
                var n2 = m.Groups["host"].Value + m.Groups[1].Value;
                var o = xs.DefaultIfEmpty(0).First();
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

    [Fact]
    public Task Calls_guarded_by_ContainsKey_are_allowed() => Verify("""
        using System.Collections.Generic;
        using System.Collections.Immutable;

        static class C
        {
            static int M(IReadOnlyDictionary<string, int> d, ImmutableDictionary<string, int> m, string k)
            {
                var a = d.ContainsKey(k) ? d[k] : 0;
                var b = !d.ContainsKey(k) ? 0 : d[k];
                var c = d.ContainsKey(k) && d[k] > 0;
                var e = !d.ContainsKey(k) || d[k] > 0;
                var f = m.ContainsKey(k) ? m : m.Add(k, 1);
                if (d.ContainsKey(k)) { return a + d[k]; }
                return a + b + (c ? 1 : 0) + (e ? 1 : 0) + f.Count;
            }
        }
        """);

    [Fact]
    public Task Guards_on_another_key_or_dictionary_dont_count() => Verify("""
        using System.Collections.Generic;

        static class C
        {
            static int M(IReadOnlyDictionary<string, int> d, IReadOnlyDictionary<string, int> other, string k, string j)
            {
                var a = d.ContainsKey(j) ? {|FP0015:d[k]|} : 0;
                var b = other.ContainsKey(k) ? {|FP0015:d[k]|} : 0;
                var c = d.ContainsKey(k) ? 0 : {|FP0015:d[k]|};
                return a + b + c;
            }
        }
        """);

    [Fact]
    public Task Unbounded_searches_are_allowed() => Verify("""
        using System.Collections.Immutable;
        using System.Linq;

        static class C
        {
            static string Free(string slug, ImmutableHashSet<string> taken) =>
                Enumerable.Range(1, int.MaxValue).Select(n => n == 1 ? slug : $"{slug}-{n}").First(c => !taken.Contains(c));
        }
        """);
}

public class FP0015_MoreSafeCalls
{
    [Fact]
    public Task Calls_that_cannot_throw_here_are_allowed() => Verify("""
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Globalization;

        static class C
        {
            static void M(string s, IReadOnlyDictionary<string, int> d)
            {
                var a = System.Convert.ToString(s, CultureInfo.InvariantCulture);
                var b = d.ToImmutableDictionary();
            }
        }
        """);

    [Fact]
    public Task Groups_kept_in_a_local_or_copied_still_have_unique_keys() => Verify("""
        using System.Collections.Immutable;
        using System.Linq;

        public sealed record Row(string Sku, decimal Price);

        public static class C
        {
            // Found by the agent eval: grouping once, checking for duplicates, then building the dictionary.
            public static ImmutableDictionary<string, decimal> A(ImmutableArray<Row> rows)
            {
                var bySku = rows.GroupBy(static row => row.Sku).ToImmutableArray();
                return bySku.ToImmutableDictionary(static group => group.Key, static group => group.First().Price);
            }

            public static ImmutableDictionary<string, int> B(ImmutableArray<string> skus) =>
                skus.Distinct().Where(static s => s.Length > 0).OrderBy(static s => s).ToImmutableDictionary(static s => s, static s => s.Length);

            // A local that isn't distinct is still reported.
            public static ImmutableDictionary<string, decimal> D(ImmutableArray<Row> rows)
            {
                var copy = rows.Where(static row => row.Price > 0).ToImmutableArray();
                return {|FP0015:copy.ToImmutableDictionary(static row => row.Sku, static row => row.Price)|};
            }
        }
        """);

    [Fact]
    public Task Copying_a_dictionary_with_a_new_comparer_can_still_collide() => Verify("""
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;

        static class C
        {
            static object M(IReadOnlyDictionary<string, int> d) => {|FP0015:d.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)|};
        }
        """);
}

public class FP0015_UniqueKeysNeedTheRightKey
{
    [Fact]
    public Task A_new_key_after_Distinct_or_GroupBy_can_still_collide() => Verify("""
        using System;
        using System.Collections.Immutable;
        using System.Linq;

        public sealed record Row(string Sku, decimal Price);

        public static class C
        {
            public static object A(ImmutableArray<string> xs) => {|FP0015:xs.Distinct().ToImmutableDictionary(s => s.Length)|};
            public static object B(ImmutableArray<string> xs) => {|FP0015:xs.GroupBy(s => s).ToImmutableDictionary(g => g.Count(), g => g.Key)|};
            public static object D(ImmutableArray<string> xs) => {|FP0015:xs.Distinct().ToImmutableDictionary(s => s.ToUpperInvariant())|};
            public static object E(ImmutableArray<Row> rows) => {|FP0015:rows.DistinctBy(r => r.Sku).ToImmutableDictionary(r => r.Price)|};
            public static object F(ImmutableArray<string> xs) =>
                {|FP0015:xs.Distinct().ToImmutableDictionary(s => s, StringComparer.OrdinalIgnoreCase)|};
            public static object G(ImmutableArray<string> xs) =>
                {|FP0015:xs.GroupBy(s => s, StringComparer.Ordinal).ToImmutableDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase)|};
            public static object H(ImmutableArray<string> xs)
            {
                var groups = xs.GroupBy(s => s.Length).ToImmutableArray();
                return {|FP0015:groups.ToImmutableDictionary(g => g.First(), g => g.Key)|};
            }
            public static object I(ImmutableArray<Row> rows) =>
                {|FP0015:rows.GroupBy(r => r.Sku, (sku, items) => new { Key = items.Count() }).ToImmutableDictionary(x => x.Key)|};
        }
        """);

    [Fact]
    public Task The_key_the_items_were_made_unique_by_is_allowed() => Verify("""
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;

        public sealed record Row(string Sku, decimal Price);
        public sealed record Site(string Method, string Sites);

        public static class C
        {
            public static object A(ImmutableArray<string> xs) => xs.Distinct().ToImmutableDictionary(s => s, s => s.Length);
            public static object B(ImmutableArray<Row> rows) => rows.DistinctBy(r => r.Sku).ToImmutableDictionary(row => row.Sku, row => row.Price);
            public static object D(ImmutableArray<Row> rows) => rows.DistinctBy(r => r.Sku).ToImmutableDictionary(r => r);
            public static object E(ImmutableArray<Row> rows) => rows.GroupBy(r => r.Sku).ToImmutableDictionary(g => g.Key, g => g.First().Price);
            public static object F(ImmutableArray<Row> rows) => rows.ToLookup(r => r.Sku).ToImmutableDictionary(g => g.Key, g => g.Count());
            public static object G(ImmutableArray<Site> sites, IEqualityComparer<string> comparer) =>
                sites.GroupBy(x => x.Method, comparer).ToImmutableDictionary(g => g.Key, g => g.First().Sites, comparer);
            public static object H(ImmutableArray<string> xs) =>
                xs.Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableDictionary(s => s, StringComparer.OrdinalIgnoreCase);
            public static object I(ImmutableArray<Row> rows) =>
                rows.GroupBy(r => r.Sku, r => r.Price).Where(g => g.Count() > 1).ToImmutableDictionary(g => g.Key, g => g.Sum());
        }
        """);
}
