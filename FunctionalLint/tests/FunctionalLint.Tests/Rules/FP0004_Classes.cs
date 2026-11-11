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
    public async Task Top_level_statements_do_not_report_the_hidden_Program_class()
    {
        var findings = await Diagnose("""
            var total = 1 + 2;
            return total;
            """, kind: OutputKind.ConsoleApplication);
        Assert.Empty(findings);
    }
}
