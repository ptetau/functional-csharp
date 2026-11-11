using Functional;
using Xunit;

namespace Functional.Tests;

public class ValidationTests
{
    private sealed record Person(string Name, int Age);

    private static Validation<string, string> Name(string s) => Validation.Check(s, s.Length > 0, "Name is required");
    private static Validation<string, int> Age(int n) => Validation.Check(n, n is >= 0 and < 150, "Age must be 0 to 149");

    [Fact]
    public void Combine_keeps_every_error()
    {
        var bad = Validation.Combine(Name(""), Age(200), (n, a) => new Person(n, a));
        Assert.Equal(["Name is required", "Age must be 0 to 149"], bad.ErrorsOrEmpty);

        var good = Validation.Combine(Name("Ada"), Age(36), (n, a) => new Person(n, a));
        Assert.Equal(new Validation<string, Person>.Valid(new Person("Ada", 36)), good);
    }

    [Fact]
    public void All_collects_errors_from_every_item()
    {
        var result = new[] { 1, 200, 2, -1 }.Traverse(Age);
        Assert.Equal(["Age must be 0 to 149", "Age must be 0 to 149"], result.ErrorsOrEmpty);
        Assert.Equal([1, 2], new[] { 1, 2 }.Traverse(Age).Match(v => v, _ => []));
    }

    [Fact]
    public void Bind_stops_at_the_first_invalid_step() =>
        Assert.Equal(["Name is required"], Name("").Bind(n => Age(-1).Map(a => (n, a))).ErrorsOrEmpty);

    [Fact]
    public void Switch_expressions_over_it_are_exhaustive() =>
        Assert.Equal("ok", Name("x") switch
        {
            Validation<string, string>.Valid => "ok",
            Validation<string, string>.Invalid => "bad",
        });
}
