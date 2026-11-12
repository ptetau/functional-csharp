using Functional;
using Xunit;

namespace Functional.Tests;

public class MoreTests
{
    [Fact]
    public void Validation_combines_up_to_six_inputs_keeping_every_error()
    {
        Validation<string, int> ok(int n) => n;
        Validation<string, int> bad(string e) => Validation.Invalid<string, int>(e);

        Assert.Equal(21, Validation.Combine(ok(1), ok(2), ok(3), ok(4), ok(5), ok(6), (a, b, c, d, e, f) => a + b + c + d + e + f)
            .Match(v => v, _ => 0));
        Assert.Equal(["x", "y"], Validation.Combine(ok(1), bad("x"), ok(3), ok(4), bad("y"), (a, b, c, d, e) => a).ErrorsOrEmpty);
    }

    [Fact]
    public void FirstOk_tries_alternatives_in_order()
    {
        Result<int> fail(string m) => new Error("x", m);
        Assert.Equal(Result.Ok(2), new[] { fail("a"), Result.Ok(2), Result.Ok(3) }.FirstOk());
        Assert.Equal("b", new[] { fail("a"), fail("b") }.FirstOk().Match(_ => "", e => e.Message));
        Assert.False(Array.Empty<Result<int>>().FirstOk().IsOk);
        Assert.Equal(Result.Ok(9), fail("a").OrElse(() => 9));
    }

    [Fact]
    public void Result_Combine_stops_at_the_first_error()
    {
        Assert.Equal(Result.Ok(3), Result.Combine(Result.Ok(1), Result.Ok(2), (a, b) => a + b));
        Assert.Equal("first", Result.Combine(Result.Fail<int>("e", "first"), Result.Fail<int>("e", "second"), (a, b) => a + b)
            .Match(_ => "", e => e.Message));
    }

    [Fact]
    public void From_counts_up_without_running_past_int_max()
    {
        Assert.Equal([2, 3, 4], Sequences.From(2).Take(3));
        Assert.Equal(int.MaxValue, Sequences.From(int.MaxValue).Single());
        Assert.Equal(-1, Sequences.From(-1).First());
    }
}
