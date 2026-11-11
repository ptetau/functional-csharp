using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace FunctionalLint.Tests;

// Markup: {|FP0001:text|} means "expect diagnostic FP0001 on exactly this text".
public class FunctionalAnalyzerTests
{
    private const string AdapterAttribute =
        "public sealed class ImperativeAdapterAttribute : System.Attribute { }";

    private static Task Verify(string source, string? config = null)
    {
        var test = new CSharpAnalyzerTest<FunctionalAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        if (config is not null)
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n\n[*]\n" + config + "\n"));
        return test.RunAsync();
    }

    [Fact]
    public Task FP0001_loops_are_flagged() => Verify("""
        static class C
        {
            static void M(int[] xs)
            {
                {|FP0001:for|} (var i = 0; i < 3; {|FP0002:i++|}) { }
                {|FP0001:foreach|} (var x in xs) { }
                {|FP0001:while|} (false) { }
                {|FP0001:do|} { } while (false);
            }
        }
        """);

    [Fact]
    public Task FP0002_reassignment_and_mutation_are_flagged() => Verify("""
        static class C
        {
            static int M(int[] xs)
            {
                var x = 1;
                {|FP0002:x = 2|};
                {|FP0002:x += 1|};
                {|FP0002:xs[0] = x|};
                return x;
            }
        }
        """);

    [Fact]
    public Task Functional_code_is_allowed() => Verify("""
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Threading.Tasks;

        public record Point(int X, int Y);
        public sealed record Box { public int W { get; init; } }

        public readonly struct Money
        {
            private readonly decimal _amount;
            public Money(decimal amount) { _amount = amount; }
            public decimal Amount => _amount;
        }

        public static class F
        {
            private static readonly int Zero = 0;
            public static Point Move(Point p) => p with { X = p.X + 1 };
            public static Box Make() => new Box { W = 3 };
            public static object Anon() => new { A = 1 };
            public static int Total(IReadOnlyList<int> xs, ImmutableArray<int> ys) => xs.Count + ys.Length;
            public static async Task<int> Later() { await Task.Yield(); return 1; }
            public static int Sum((int, int) t)
            {
                var (a, b) = t;
                (var c, var d) = t;
                _ = c;
                return a + b + d + Zero;
            }
        }
        """);

    [Fact]
    public Task FP0003_FP0004_classes_and_mutable_members_are_flagged() => Verify("""
        public class {|FP0004:Account|}
        {
            private int {|FP0003:_balance|};
            public int {|FP0003:Balance|} { get; set; }
        }
        """);

    [Fact]
    public Task Attributes_are_allowed() => Verify("""
        public sealed class MarkerAttribute : System.Attribute { }
        """);

    [Fact]
    public Task FP0005_creating_mutable_collections_is_flagged() => Verify("""
        using System.Collections.Generic;
        using System.Text;

        static class C
        {
            static void M()
            {
                var xs = {|FP0005:new List<int>()|};
                List<int> ys = {|FP0005:[1, 2]|};
                var sb = {|FP0005:new StringBuilder()|};
            }
        }
        """);

    [Fact]
    public Task FP0006_FP0009_legacy_code_only_through_adapters() => Verify($$"""
        namespace Legacy
        {
            public class OrderService
            {
                private int _count;
                public int Next() { _count++; return _count; }
            }
        }

        namespace App
        {
            {{AdapterAttribute}}

            [ImperativeAdapter]
            public sealed class Orders
            {
                private readonly Legacy.OrderService _service;
                public Orders(Legacy.OrderService service) { _service = service; }

                public int NextTwo()
                {
                    var total = 0;
                    for (var i = 0; i < 2; i++) total += _service.Next();
                    return total;
                }

                public Legacy.OrderService {|FP0009:Raw|}() => _service;
            }

            public static class Report
            {
                public static int Direct() => {|FP0006:{|FP0006:new Legacy.OrderService()|}.Next()|};
                public static int ViaAdapter(Orders orders) => orders.NextTwo();
            }
        }
        """,
        "functional_lint.legacy_namespaces = Legacy");

    [Fact]
    public Task FP0007_throwing_is_flagged_outside_adapters() => Verify($$"""
        {{AdapterAttribute}}

        public static class C
        {
            public static int Length(string s) => s.Length > 0 ? s.Length : {|FP0007:throw|} new System.ArgumentException("empty");
            public static void Fail() { {|FP0007:throw|} new System.InvalidOperationException(); }
        }

        [ImperativeAdapter]
        public static class Edge
        {
            public static void Fail() => throw new System.InvalidOperationException();
        }
        """);

    [Fact]
    public Task FP0008_mutable_types_in_signatures_are_flagged() => Verify("""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public static class C
        {
            public static int Count(IList<int> {|FP0008:xs|}) => xs.Count;
            public static Task<Dictionary<string, int>> {|FP0008:Load|}() => null!;
            public static readonly ICollection<int> {|FP0008:Shared|} = null!;
        }
        """);

    [Fact]
    public Task FP0010_async_void_is_flagged() => Verify("""
        using System;
        using System.Threading.Tasks;

        public static class C
        {
            public static async void {|FP0010:Fire|}() => await Task.Yield();
            public static Action Make() => {|FP0010:async|} () => await Task.Yield();
        }
        """);

    [Fact]
    public Task FP0011_side_effects_are_flagged_outside_adapters() => Verify($$"""
        using System;
        using System.IO;

        {{AdapterAttribute}}

        public static class C
        {
            public static DateTime When() => {|FP0011:DateTime.Now|};
            public static Guid Id() => {|FP0011:Guid.NewGuid()|};
            public static void Log(string s) => {|FP0011:Console.WriteLine(s)|};
            public static string Temp() => {|FP0011:Path.GetTempPath()|};
            public static DateTime Tomorrow(DateTime today) => today.AddDays(1);
        }

        [ImperativeAdapter]
        public static class Clock
        {
            public static DateTime Now() => DateTime.Now;
        }
        """,
        "functional_lint.impure_apis = System.IO.Path.GetTempPath");
}
