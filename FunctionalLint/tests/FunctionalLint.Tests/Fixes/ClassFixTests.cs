using System.Threading.Tasks;
using FunctionalLint.CodeFixes;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Fixes;

public class ClassFixTests
{
    [Fact]
    public Task A_class_of_static_helpers_becomes_a_static_class() => VerifyFix<ClassFix>("""
        public sealed class {|FP0004:Maths|}
        {
            public const int Zero = 0;
            public static int Double(int x) => x * 2;
        }

        class {|FP0004:Empty|} { }
        """, """
        public static class Maths
        {
            public const int Zero = 0;
            public static int Double(int x) => x * 2;
        }

        static class Empty { }
        """);

    [Fact]
    public Task A_data_class_becomes_a_record_and_its_setters_are_still_reported() => VerifyFix<ClassFix>("""
        using System;

        [Serializable]
        public sealed class {|FP0004:Order|}
        {
            public int Id { get; init; }
            public string {|FP0003:Name|} { get; set; } = "";
        }
        """, """
        using System;

        [Serializable]
        public sealed record Order
        {
            public int Id { get; init; }
            public string {|FP0003:Name|} { get; set; } = "";
        }
        """);

    [Fact]
    public Task Generic_classes_keep_their_type_parameters() => VerifyFix<ClassFix>("""
        public class {|FP0004:Box|}<T> where T : notnull
        {
            public T Value { get; init; } = default!;
        }
        """, """
        public record Box<T> where T : notnull
        {
            public T Value { get; init; } = default!;
        }
        """);

    [Fact]
    public Task Classes_that_would_change_meaning_are_left_alone() => VerifyFix<ClassFix>("""
        using System.Collections.Generic;

        // A primary constructor would turn 'items' into a public property.
        public class {|FP0004:Cart|}(int items) { public int Count => items; }

        // A record can't inherit from a class.
        public class {|FP0004:Error|} : System.Exception { }

        // Records write their own Equals(object).
        public class {|FP0004:Id|}
        {
            public override bool Equals(object? obj) => obj is Id;
            public override int GetHashCode() => 0;
        }

        // Fixing one part of a partial class would leave the others behind.
        public partial class {|FP0004:Split|} { }
        public partial class Split { }
        """, """
        using System.Collections.Generic;

        // A primary constructor would turn 'items' into a public property.
        public class {|FP0004:Cart|}(int items) { public int Count => items; }

        // A record can't inherit from a class.
        public class {|FP0004:Error|} : System.Exception { }

        // Records write their own Equals(object).
        public class {|FP0004:Id|}
        {
            public override bool Equals(object? obj) => obj is Id;
            public override int GetHashCode() => 0;
        }

        // Fixing one part of a partial class would leave the others behind.
        public partial class {|FP0004:Split|} { }
        public partial class Split { }
        """);
}

public class ClassFixSafetyTests
{
    [Fact]
    public Task Services_with_constructors_or_private_state_are_left_alone() => VerifyFix<ClassFix>("""
        public class {|FP0004:Calculator|}
        {
            private readonly int _rate;
            public Calculator(int rate) { _rate = rate; }
            public int Apply(int x) => x * _rate;
        }
        """, """
        public class {|FP0004:Calculator|}
        {
            private readonly int _rate;
            public Calculator(int rate) { _rate = rate; }
            public int Apply(int x) => x * _rate;
        }
        """);
}
