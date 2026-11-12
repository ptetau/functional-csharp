using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0013_ExhaustiveSwitches
{
    private const string Types = """
        public enum Colour { Red, Green, Blue }

        public closed record Shape;
        public sealed record Circle(double Radius) : Shape;
        public sealed record Square(double Side) : Shape;

        public record Cat(string Name);
        public record Dog(string Name);
        public union Pet(Cat, Dog);
        """;

    [Fact]
    public Task Listing_every_case_is_allowed() => Verify($$"""
        {{Types}}

        public static class Use
        {
            public static int A(Colour c) => c switch { Colour.Red => 1, Colour.Green => 2, Colour.Blue => 3 };
            public static double B(Shape s) => s switch { Circle c => c.Radius, Square q => q.Side };
            public static string C(Pet p) => p switch { Cat c => c.Name, Dog d => d.Name };
        }
        """);

    [Fact]
    public Task Catch_all_arms_over_enums_closed_types_and_unions_are_flagged() => Verify($$"""
        {{Types}}

        public static class Use
        {
            public static int A(Colour c) => c switch { Colour.Red => 1, {|FP0013:_|} => 0 };
            public static double B(Shape s) => s switch { Circle c => c.Radius, {|FP0013:var other|} => 0 };
            public static double C(Shape s) => s switch { Circle c => c.Radius, {|FP0013:Shape|} => 0 };
            public static string D(Pet p) => p switch { Cat c => c.Name, {|FP0013:_|} => "" };
        }
        """);

    [Fact]
    public Task Switch_statements_over_checked_types_are_flagged() => Verify($$"""
        {{Types}}

        public static class Use
        {
            public static int A(Colour c)
            {
                {|FP0013:switch|} (c) { case Colour.Red: return 1; default: return 0; }
            }

            public static int B(int n)
            {
                switch (n) { case 1: return 1; default: return 0; }
            }
        }
        """);

    [Fact]
    public Task Catch_alls_are_fine_where_cases_cannot_be_listed() => Verify("""
        [System.Flags] public enum Access { None = 0, Read = 1, Write = 2 }
        public abstract record Open;
        public sealed record One : Open;

        public static class Use
        {
            public static string A(int n) => n switch { 1 => "one", _ => "many" };
            public static string B(string s) => s switch { "a" => "A", var other => other };
            public static string C(Access a) => a switch { Access.Read => "r", _ => "other" };
            public static string D(Open o) => o switch { One => "one", _ => "other" };
            public static string E(Colour c) => c switch { Colour.Red => "red", _ when c > Colour.Red => "later", Colour.Green => "green", Colour.Blue => "blue" };
        }

        public enum Colour { Red, Green, Blue }
        """);

    [Fact]
    public Task Adapters_are_exempt() => Verify($$"""
        {{Types}}

        {{AdapterAttribute}}

        [ImperativeAdapter]
        public static class Edge
        {
            public static int A(Colour c) => c switch { Colour.Red => 1, _ => 0 };
        }
        """);
}
