using System.Diagnostics;
using Functional;
using Xunit;

namespace Functional.Tests;

public class ResultOptionTests
{
    [Fact]
    public void All_returns_the_values_or_the_first_error()
    {
        Assert.Equal([1, 2], new Result<int>[] { 1, 2 }.All().ValueOr([]));
        var failed = new Result<int>[] { 1, new Error("a", "first"), new Error("b", "second") }.All();
        Assert.Equal("first", failed.Match(_ => "", e => e.Message));
    }

    [Fact]
    public void All_is_linear()
    {
        // The old Aggregate + ImmutableArray.Add version took seconds here.
        var items = Enumerable.Range(0, 200_000).Select(i => (Result<int>)i).ToList();
        var clock = Stopwatch.StartNew();
        Assert.Equal(200_000, items.All().ValueOr([]).Length);
        Assert.True(clock.ElapsedMilliseconds < 1000, $"{clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Scan_keeps_each_running_total() =>
        Assert.Equal([1, 3, 6], new[] { 1, 2, 3 }.Scan(0, (sum, x) => sum + x));

    [Fact]
    public void MapError_adds_context() =>
        Assert.Equal("line 3: bad", Result.Fail<int>("x", "bad").MapError(e => e with { Message = "line 3: " + e.Message })
            .Match(_ => "", e => e.Message));

    [Fact]
    public void Option_supports_bind_where_and_query_syntax()
    {
        var sum = from a in Option.Some(1) from b in Option.Some(2) select a + b;
        Assert.Equal(Option.Some(3), sum);
        Assert.Equal(Option.None<int>(), Option.Some(1).Where(x => x > 1));
        Assert.Equal(Option.Some("2"), Option.Some(2).Bind(x => Option.Some(x.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Equal([5], Option.Some(5).ToValueList());
    }

    [Fact]
    public void Shells_can_act_on_a_value_without_a_dummy_return()
    {
        var seen = new List<string>();
        Option.Some("a").IfSome(seen.Add);
        Option.None<string>().IfSome(seen.Add);
        Result.Ok("b").Switch(seen.Add, e => seen.Add(e.Message));
        Assert.Equal(["a", "b"], seen);
    }
}
