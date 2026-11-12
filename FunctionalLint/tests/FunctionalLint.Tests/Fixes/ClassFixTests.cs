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

public class ClassFixEqualityTests
{
    // A record compares by value, a class by reference: Distinct() over two equal tags gave 2 and would give 1.
    [Fact]
    public Task Classes_whose_instances_are_compared_are_left_alone()
    {
        const string source = """
            using System.Collections.Immutable;
            using System.Linq;

            public class {|FP0004:Tag|} { public string Name { get; init; } = ""; }
            public class {|FP0004:Sku|} { public string Code { get; init; } = ""; }
            public class {|FP0004:Price|} { public decimal Amount { get; init; } }
            public class {|FP0004:Region|} { public string Name { get; init; } = ""; }
            public class {|FP0004:Colour|} { public string Name { get; init; } = ""; }
            public class {|FP0004:Size|} { public int Value { get; init; } }
            public class {|FP0004:Note|} { public string Text { get; init; } = ""; }
            public class {|FP0004:Unit|} { public string Name { get; init; } = ""; }
            public sealed record Line(Unit Unit, int Count);

            public static class Use
            {
                public static int A(ImmutableArray<Tag> tags) => tags.Distinct().Count();
                public static bool B(Sku a, Sku b) => a == b;
                public static bool D(Price a, Price b) => a.Equals(b);
                public static ImmutableHashSet<Region> E(ImmutableArray<Region> regions) => regions.ToImmutableHashSet();
                public static int F(ImmutableArray<(Colour Colour, int N)> xs) => xs.GroupBy(x => x.Colour).Count();
                public static bool G(ImmutableArray<Size> sizes, Size s) => sizes.Contains(s);
                public static bool H((Note, int) a, (Note, int) b) => a == b;
                public static bool I(Line a, Line b) => a == b;
            }
            """;
        return VerifyFix<ClassFix>(source, source);
    }

    [Fact]
    public Task Classes_that_are_only_built_read_and_grouped_by_a_member_still_become_records() => VerifyFix<ClassFix>("""
        using System.Collections.Immutable;
        using System.Linq;

        public class {|FP0004:Order|} { public string Customer { get; init; } = ""; }

        public static class Use
        {
            public static ImmutableArray<Order> A(ImmutableArray<Order> orders, Order o) => orders.Add(o);
            public static int B(ImmutableArray<Order> orders) => orders.GroupBy(o => o.Customer).Count();
            public static ImmutableDictionary<string, Order> D(ImmutableArray<Order> orders) =>
                orders.GroupBy(o => o.Customer).ToImmutableDictionary(g => g.Key, g => g.Last());
            public static bool E(Order? o) => o == null;
        }
        """, """
        using System.Collections.Immutable;
        using System.Linq;

        public record Order { public string Customer { get; init; } = ""; }

        public static class Use
        {
            public static ImmutableArray<Order> A(ImmutableArray<Order> orders, Order o) => orders.Add(o);
            public static int B(ImmutableArray<Order> orders) => orders.GroupBy(o => o.Customer).Count();
            public static ImmutableDictionary<string, Order> D(ImmutableArray<Order> orders) =>
                orders.GroupBy(o => o.Customer).ToImmutableDictionary(g => g.Key, g => g.Last());
            public static bool E(Order? o) => o == null;
        }
        """);

    [Fact]
    public Task Classes_with_only_static_members_still_become_static_whatever_compares_them() => VerifyFix<ClassFix>("""
        public class {|FP0004:Maths|} { public static int Double(int x) => x * 2; }
        public static class Use { public static bool Same(object a) => a.Equals(typeof(Maths)); }
        """, """
        public static class Maths { public static int Double(int x) => x * 2; }
        public static class Use { public static bool Same(object a) => a.Equals(typeof(Maths)); }
        """);
}

public class ClassFixLayoutTests
{
    [Fact]
    public Task Hand_aligned_code_keeps_its_layout() => VerifyFix<ClassFix>("""
        public class {|FP0004:Point|}
        {
            public int X     { get; init; }
            public int Width { get; init; }
        }
        """, """
        public record Point
        {
            public int X     { get; init; }
            public int Width { get; init; }
        }
        """);
}
