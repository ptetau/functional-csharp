using System.Collections.Immutable;
using Functional;
using Xunit;

namespace Functional.Tests;

public class ValueListTests
{
    private sealed record Order(string Id, ValueList<string> Tags);
    private sealed record WithArray(ImmutableArray<string> Tags);

    [Fact]
    public void Records_holding_a_ValueList_are_equal_when_the_contents_are()
    {
        Assert.Equal(new Order("a", ["x", "y"]), new Order("a", ["x", "y"]));
        Assert.NotEqual(new Order("a", ["x", "y"]), new Order("a", ["y", "x"]));
        Assert.Equal(new Order("a", ["x"]).GetHashCode(), new Order("a", ["x"]).GetHashCode());
    }

    [Fact]
    public void Records_holding_an_ImmutableArray_are_not_which_is_why_ValueList_exists() =>
        Assert.NotEqual(new WithArray(["x"]), new WithArray(["x"]));

    [Fact]
    public void Default_is_empty_and_safe_to_use()
    {
        ValueList<int> none = default;
        Assert.True(none.IsEmpty);
        Assert.Equal([1], none.Add(1));
        Assert.Equal(ValueList.Empty<int>(), new ValueList<int>(ImmutableArray<int>.Empty));
    }

    [Fact]
    public void Changes_return_a_new_list()
    {
        ValueList<int> xs = [1, 2, 3];
        var ys = xs.Add(4).RemoveAt(0).SetItem(0, 9);
        Assert.Equal([1, 2, 3], xs);
        Assert.Equal([9, 3, 4], ys);
    }

    [Fact]
    public void Converts_to_and_from_ImmutableArray()
    {
        ImmutableArray<int> array = [1, 2];
        ValueList<int> list = array;
        ImmutableArray<int> back = list;
        Assert.Equal(array, back);
        Assert.Equal([1, 2], new[] { 1, 2 }.ToValueList());
    }

    [Fact]
    public void ToString_shows_the_items() => Assert.Equal("[1, 2]", ValueList.Create<int>([1, 2]).ToString());

    [Fact]
    public void Round_trips_through_System_Text_Json()
    {
        var order = new Order("a", ["x", "y"]);
        var json = System.Text.Json.JsonSerializer.Serialize(order);
        Assert.Equal("""{"Id":"a","Tags":["x","y"]}""", json);
        Assert.Equal(order, System.Text.Json.JsonSerializer.Deserialize<Order>(json));
        Assert.Equal(new Order("b", []), System.Text.Json.JsonSerializer.Deserialize<Order>("""{"Id":"b","Tags":null}"""));
        Assert.Equal(new Order("c", []), System.Text.Json.JsonSerializer.Deserialize<Order>("""{"Id":"c"}"""));
    }

    [Fact]
    public void Nested_lists_compare_by_contents_too() =>
        Assert.Equal(new ValueList<ValueList<int>>([[1], [2, 3]]), new ValueList<ValueList<int>>([[1], [2, 3]]));
}

public class ValueMapTests
{
    private sealed record Prices(ValueMap<string, decimal> BySku);

    [Fact]
    public void Records_holding_a_ValueMap_are_equal_when_the_contents_are()
    {
        var a = new Prices(new[] { ("x", 1m), ("y", 2m) }.ToValueMap(p => p.Item1, p => p.Item2));
        var b = new Prices(new[] { ("y", 2m), ("x", 1m) }.ToValueMap(p => p.Item1, p => p.Item2));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new Prices(a.BySku.SetItem("x", 9m)));
    }

    [Fact]
    public void Equal_maps_hash_the_same_even_with_a_custom_comparer()
    {
        var ignoreCase = System.Collections.Immutable.ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase);
        ValueMap<string, int> upper = ignoreCase.SetItem("A", 1);
        ValueMap<string, int> lower = ignoreCase.SetItem("a", 1);
        Assert.Equal(upper, lower);
        Assert.Equal(upper.GetHashCode(), lower.GetHashCode());
        Assert.Contains(lower, new HashSet<ValueMap<string, int>> { upper });

        // Different comparers: unequal both ways, so equality stays symmetric.
        ValueMap<string, int> exact = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty.SetItem("a", 1);
        Assert.False(lower.Equals(exact));
        Assert.False(exact.Equals(lower));
    }

    [Fact]
    public void Lookups_and_building_never_throw()
    {
        var map = new[] { ("x", 1), ("x", 2) }.ToValueMap(p => p.Item1, p => p.Item2);
        Assert.Equal(Option.Some(2), map.Find("x"));
        Assert.Equal(Option.None<int>(), map.Find("missing"));
        Assert.True(default(ValueMap<string, int>).IsEmpty);
    }

    [Fact]
    public void Round_trips_through_System_Text_Json()
    {
        var prices = new Prices(new[] { ("x", 1.5m) }.ToValueMap(p => p.Item1, p => p.Item2));
        var json = System.Text.Json.JsonSerializer.Serialize(prices);
        Assert.Equal("""{"BySku":{"x":1.5}}""", json);
        Assert.Equal(prices, System.Text.Json.JsonSerializer.Deserialize<Prices>(json));
    }
}

public class ValueListPatternTests
{
    [Fact]
    public void Aggregate_any_and_all_dont_allocate_per_call()
    {
        // LINQ over the struct would box it and allocate an enumerator on every call (~100 bytes each).
        ValueList<int> xs = [1, 2, 3, 4];
        static int Run(ValueList<int> xs) =>
            xs.Aggregate(0, static (sum, x) => sum + x) + (xs.Any(static x => x > 3) ? 1 : 0) + (xs.All(static x => x > 0) ? 1 : 0);
        Assert.Equal(12, Run(xs));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var total = Enumerable.Range(0, 10_000).Sum(_ => Run(xs));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(120_000, total);
        Assert.True(allocated < 10_000, $"{allocated} bytes for 10,000 calls");
    }

    [Fact]
    public void List_patterns_with_a_slice_work()
    {
        ValueList<int> xs = [1, 2, 3];
        Assert.True(xs is [1, .. var rest] && rest == [2, 3]);
    }
}
