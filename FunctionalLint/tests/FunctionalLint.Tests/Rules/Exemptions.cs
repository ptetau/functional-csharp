using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

/// <summary>Where the rules switch off: [ImperativeAdapter] and legacy namespaces.</summary>
public class Exemptions
{
    [Fact]
    public Task An_adapter_attribute_on_a_method_exempts_only_that_method() => Verify($$"""
        {{AdapterAttribute}}

        public static class C
        {
            [ImperativeAdapter]
            public static void Loud() => System.Console.WriteLine("hi");

            public static void Quiet() => {|FP0011:System.Console.WriteLine("hi")|};
        }
        """);

    [Fact]
    public Task An_adapter_attribute_on_a_property_exempts_its_accessors() => Verify($$"""
        {{AdapterAttribute}}

        public static class C
        {
            [ImperativeAdapter]
            public static System.DateTime Now => System.DateTime.Now;
        }
        """);

    [Fact]
    public Task Nested_types_and_lambdas_inside_an_adapter_are_exempt() => Verify($$"""
        using System;
        using System.Collections.Generic;

        {{AdapterAttribute}}

        [ImperativeAdapter]
        public static class Outer
        {
            public class Helper { public int Count; }
            public static Func<int> Make() => () => { var xs = new List<int>(); for (var i = 0; i < 3; i++) xs.Add(i); return xs.Count; };
        }
        """);

    [Fact]
    public Task The_attribute_is_matched_by_name_in_any_namespace() => Verify("""
        namespace My.Infrastructure
        {
            public sealed class ImperativeAdapterAttribute : System.Attribute { }
        }

        namespace App
        {
            [My.Infrastructure.ImperativeAdapter]
            public class Gateway { public int Count { get; set; } }
        }
        """);
}
