using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0001_Loops
{
    [Fact]
    public Task Every_loop_kind_is_flagged_on_its_keyword_and_its_variables_are_not_reported_again() => Verify("""
        static class C
        {
            static void M(int[] xs)
            {
                var total = 0;
                {|FP0001:for|} (var i = 0; i < 3; i++) { total += i; }
                {|FP0001:foreach|} (var x in xs) { }
                {|FP0001:while|} (false) { }
                {|FP0001:do|} { } while (false);
            }
        }
        """);

    [Fact]
    public Task Await_foreach_is_reported_on_foreach_not_await() => Verify("""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        static class C
        {
            static async Task M(IAsyncEnumerable<int> xs)
            {
                await {|FP0001:foreach|} (var x in xs) { }
            }
        }
        """);

    [Fact]
    public Task Loops_inside_lambdas_and_local_functions_are_flagged() => Verify("""
        using System;

        static class C
        {
            static readonly Action A = () => { {|FP0001:while|} (false) { } };

            static void M()
            {
                void Local() { {|FP0001:for|} (;;) { } }
                Local();
            }
        }
        """);

    [Fact]
    public Task Linq_queries_and_recursion_are_allowed() => Verify("""
        using System;
        using System.Collections.Immutable;
        using System.Linq;

        static class C
        {
            static int Sum(ImmutableArray<int> xs) => xs.Aggregate(0, (total, x) => total + x);
            static ImmutableArray<int> Evens(ImmutableArray<int> xs) =>
                (from x in xs where x % 2 == 0 select x).ToImmutableArray();
            static int Depth(Node? n) => n is null ? 0 : 1 + Math.Max(Depth(n.Left), Depth(n.Right));
        }

        record Node(int Value, Node? Left, Node? Right);
        """);
}

public class FP0001_LoopsWrittenAsCalls
{
    [Fact]
    public Task ForEach_calls_and_goto_are_loops() => Verify("""
        using System.Collections.Generic;
        using System.Collections.Immutable;

        static class C
        {
            static void M(ImmutableList<int> xs, int[] ys)
            {
                xs.{|FP0001:ForEach|}(x => System.Console.Write(""));
                System.Array.{|FP0001:ForEach|}(ys, y => { });
            again:
                {|FP0001:goto|} again;
            }
        }
        """.Replace("System.Console.Write(\"\")", "_ = x"));
}
