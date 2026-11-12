using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0004_Classes
{
    [Fact]
    public Task Plain_abstract_sealed_generic_and_nested_classes_are_flagged() => Verify("""
        public class {|FP0004:Plain|} { }
        public abstract class {|FP0004:Base|} { }
        public sealed class {|FP0004:Final|} { }
        public class {|FP0004:Box|}<T> { }
        public static class Outer { public class {|FP0004:Inner|} { } }
        """);

    [Fact]
    public Task Plug_ins_extending_a_library_abstract_class_are_allowed() => Verify("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public sealed class DateConverter : JsonConverter<DateOnly>
        {
            public override DateOnly Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => default;
            public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) { }
        }

        public abstract class {|FP0004:Shape|} { }
        public sealed class {|FP0004:Circle|} : Shape { }
        public class {|FP0004:Plain|} : Exception { }
        """);

    [Fact]
    public Task A_partial_class_is_reported_once() => Verify("""
        public partial class {|FP0004:Split|} { }
        public partial class Split { }
        """);

    [Fact]
    public Task Exception_classes_are_flagged_because_throwing_is_banned_too() => Verify("""
        public class {|FP0004:NotFound|} : System.Exception { }
        """);

    [Fact]
    public Task Records_static_classes_and_other_type_kinds_are_allowed() => Verify("""
        public record Person(string Name);
        public sealed record Order { public int Id { get; init; } }
        public static class Maths { public static int Double(int x) => x * 2; }
        public interface IShape { double Area(); }
        public struct Size { }
        public enum Colour { Red, Green }
        public delegate int Op(int x);
        public sealed class MarkerAttribute : System.Attribute { }
        """);

    [Fact]
    public Task Closed_records_and_unions_are_allowed() => Verify("""
        public closed record Shape;
        public sealed record Circle(double Radius) : Shape;
        public sealed record Square(double Side) : Shape;

        public record Cat(string Name);
        public record Dog(string Name);
        public union Pet(Cat, Dog);

        public static class Use
        {
            public static double Area(Shape s) => s switch
            {
                Circle c => 3.14 * c.Radius * c.Radius,
                Square q => q.Side * q.Side,
            };

            public static string Name(Pet p) => p switch
            {
                Cat c => c.Name,
                Dog d => d.Name,
            };
        }
        """);

    [Fact]
    public async Task Top_level_statements_do_not_report_the_hidden_Program_class()
    {
        var findings = await Diagnose("""
            var total = 1 + 2;
            return total;
            """, kind: OutputKind.ConsoleApplication);
        Assert.Empty(findings);
    }
}
