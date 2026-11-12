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

public class FP0013_NullableAndPatternCatchAlls
{
    [Fact]
    public Task Nullable_inputs_and_property_pattern_catch_alls_are_flagged() => Verify("""
        public enum Colour { Red, Green }
        public closed record Shape;
        public sealed record Dot : Shape;
        public sealed record Ring : Shape;

        public static class Use
        {
            public static int A(Colour? c) => c switch { Colour.Red => 1, null => 0, {|FP0013:_|} => 2 };
            public static int B(Colour c) => c switch { Colour.Red => 1, {|FP0013:{ }|} => 2 };
            public static int C(Shape? s) => s switch { Dot => 1, {|FP0013:not null|} => 2, null => 0 };
            public static int D(Colour? c)
            {
                {|FP0013:switch|} (c) { case Colour.Red: return 1; default: return 0; }
            }
            public static int E(Colour? c) => c switch { Colour.Red => 1, Colour.Green => 2, null => 0 };
        }
        """);
}

public class FP0013_DisguisedCatchAlls
{
    private const string Types = """
        public enum Colour { Red, Green, Blue }

        public interface IShape;
        public closed record Shape : IShape;
        public sealed record Circle(double Radius) : Shape;
        public sealed record Square(double Side) : Shape;

        public record Animal(string Name);
        public record Cat(string Name) : Animal(Name);
        public record Dog(string Name) : Animal(Name);
        public union Pet(Cat, Dog);
        """;

    [Fact]
    public Task Arms_that_match_every_value_are_flagged() => Verify($$"""
        {{Types}}

        public static class Use
        {
            public static int A(Colour c) => c switch { Colour.Red => 1, {|FP0013:System.Enum|} => 0 };
            public static int B(Colour c) => c switch { Colour.Red => 1, {|FP0013:object|} => 0 };
            public static int D(Colour c) => c switch { Colour.Red => 1, {|FP0013:System.IComparable x|} => 0 };
            public static double E(Shape s) => s switch { Circle c => c.Radius, {|FP0013:object|} => 0 };
            public static double F(Shape s) => s switch { Circle c => c.Radius, {|FP0013:IShape other|} => 0 };
            public static double G(Shape s) => s switch { Circle c => c.Radius, {|FP0013:_|} when true => 0 };
            public static double H(Shape s) => s switch { Circle c => c.Radius, {|FP0013:var x|} when 1 == 1 => 0 };
            public static double I(Shape s) => s switch { Circle c => c.Radius, {|FP0013:Square or _|} => 0 };
            public static string J(Pet p) => p switch { Cat c => c.Name, {|FP0013:Animal a|} => a.Name };
            public static string K(Pet p) => p switch { Cat c => c.Name, {|FP0013:object|} => "" };
        }
        """);

    [Fact]
    public Task Casting_the_input_to_a_wider_type_is_flagged() => Verify($$"""
        {{Types}}

        public static class Use
        {
            public static int A(Shape s) => {|FP0013:(object)s|} switch { Circle => 1, Square => 2, _ => 0 };
            public static int B(Colour c) => {|FP0013:(int)c|} switch { 0 => 1, _ => 0 };
            public static int D(Colour c) => {|FP0013:(System.Enum)c|} switch { Colour.Red => 1, _ => 0 };
            public static int E(Colour c)
            {
                switch ({|FP0013:(object)c|}) { case Colour.Red: return 1; default: return 0; }
            }
        }
        """);

    [Fact]
    public Task Narrower_arms_guards_and_casts_to_checked_types_are_allowed() => Verify($$"""
        {{Types}}

        public static class Use
        {
            public static int A(Colour c) => c switch { Colour.Red => 1, Colour.Green => 2, Colour.Blue => 3 };
            public static double B(Shape s) => s switch { Circle c when c.Radius > 1 => c.Radius, Circle => 1, Square q => q.Side };
            public static double D(Shape s) => s switch { Circle or Square => 1 };
            public static int E(Colour c) => c switch { Colour.Red => 1, _ when c > Colour.Red => 2, Colour.Green => 3, Colour.Blue => 4 };
            public static int F(Colour c) => (Colour?)c switch { Colour.Red => 1, Colour.Green => 2, Colour.Blue => 3, null => 0 };
            public static int G(Circle c) => (Shape)c switch { Circle => 1, Square => 2 };
            public static int H(object o) => o switch { Colour.Red => 1, _ => 0 };
            public static int I(int n) => (long)n switch { 1 => 1, _ => 0 };
            public static string J(Pet p) => p switch { Cat c => c.Name, Dog d => d.Name };
        }
        """);
}
