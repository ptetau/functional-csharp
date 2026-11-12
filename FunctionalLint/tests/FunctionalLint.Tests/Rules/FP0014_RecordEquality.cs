using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0014_RecordEquality
{
    [Fact]
    public Task Collections_that_compare_by_reference_are_flagged_in_records() => Verify("""
        using System.Collections.Generic;
        using System.Collections.Immutable;

        public sealed record Order(string Id, ImmutableArray<string> {|FP0014:Tags|})
        {
            public IReadOnlyList<int> {|FP0014:Lines|} { get; init; } = [];
            public int[] {|FP0014:Raw|} { get; init; } = [];
        }

        public readonly record struct Path(ImmutableList<string> {|FP0014:Steps|});

        public sealed record Prices(ImmutableDictionary<string, decimal> {|FP0014:BySku|});
        """);

    [Fact]
    public Task Strings_value_equal_collections_and_custom_equality_are_allowed() => Verify("""
        using System.Linq;
        using System.Collections;
        using System.Collections.Generic;
        using System.Collections.Immutable;

        public sealed class ValueEqualityAttribute : System.Attribute { }

        [ValueEquality]
        public readonly struct Bag : IEnumerable<int>
        {
            public IEnumerator<int> GetEnumerator() => Enumerable().GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            private static IEnumerable<int> Enumerable() => [];
        }

        public sealed record Named(string Name, Bag Items);

        public sealed record Custom(ImmutableArray<int> Items)
        {
            public bool Equals(Custom? other) => other is not null && System.Linq.Enumerable.SequenceEqual(Items, other.Items);
            public override int GetHashCode() => Items.Length;
        }

        public static class NotARecord
        {
            public static readonly ImmutableArray<int> Fine = [];
        }

        public static class Folds
        {
            // A fold's private running state: only compared where you can see it.
            private sealed record State(ImmutableArray<int> Seen, int Total);
            public static int Sum(ImmutableArray<int> xs) =>
                xs.Aggregate(new State([], 0), (s, x) => new State(s.Seen.Add(x), s.Total + x)).Total;
        }

        public sealed record Computed(string Csv)
        {
            public ImmutableArray<string> Parts => [.. Csv.Split(',')];
        }
        """);
}
