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
    public void Nested_lists_compare_by_contents_too() =>
        Assert.Equal(new ValueList<ValueList<int>>([[1], [2, 3]]), new ValueList<ValueList<int>>([[1], [2, 3]]));
}
