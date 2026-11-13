using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0021_HashOrder
{
    [Fact]
    public Task Order_taken_from_a_hash_set_or_dictionary_is_flagged() => Verify("""
        using System.Collections.Immutable;
        using System.Linq;

        public static class C
        {
            public static string Join(ImmutableHashSet<string> tags) => {|FP0021:string.Join(", ", tags)|};
            public static string Keys(ImmutableDictionary<string, int> map) => {|FP0021:string.Join(", ", map.Keys)|};
            public static ImmutableArray<string> Upper(ImmutableHashSet<string> tags) =>
                {|FP0021:tags.Select(static t => t.ToUpperInvariant()).ToImmutableArray()|};
            public static string? First(ImmutableHashSet<string> tags) => {|FP0021:tags.FirstOrDefault()|};
            public static ImmutableArray<string> Spread(ImmutableHashSet<string> tags) => {|FP0021:[.. tags]|};
        }
        """);

    [Fact]
    public Task Sorted_or_order_free_uses_are_fine() => Verify("""
        using System.Collections.Immutable;
        using System.Linq;

        public static class C
        {
            public static string Join(ImmutableHashSet<string> tags) => string.Join(", ", tags.Order());
            public static string Pairs(ImmutableDictionary<string, int> map) =>
                string.Join(", ", map.OrderBy(static p => p.Key).Select(static p => p.Key));
            public static int Count(ImmutableHashSet<string> tags) => tags.Count(static t => t.Length > 2);
            public static bool Has(ImmutableHashSet<string> tags) => tags.Any();
            public static ImmutableHashSet<string> Copy(ImmutableHashSet<string> tags) => [.. tags];
            public static string Sorted(ImmutableSortedSet<string> tags) => string.Join(", ", tags);
            public static string List(ImmutableArray<string> tags) => string.Join(", ", tags);
        }
        """);
}
