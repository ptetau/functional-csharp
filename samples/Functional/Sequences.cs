using System.Collections.Immutable;

namespace Functional;

public static class Sequences
{
    /// <summary>
    /// Like Aggregate, but keeps every intermediate result: a running total.
    /// <c>[1, 2, 3].Scan(0, (sum, x) =&gt; sum + x)</c> is <c>[1, 3, 6]</c>.
    /// Builds an ImmutableList (O(log n) per step), not an ImmutableArray, whose Add copies the whole array
    /// and makes the usual Aggregate-into-ImmutableArray pattern O(n²).
    /// </summary>
    public static ValueList<TAcc> Scan<T, TAcc>(this IEnumerable<T> items, TAcc seed, Func<TAcc, T, TAcc> step) =>
        items.Aggregate(
                (Last: seed, All: ImmutableList<TAcc>.Empty),
                (state, item) =>
                {
                    var next = step(state.Last, item);
                    return (next, state.All.Add(next));
                })
            .All
            .ToValueList();
}
