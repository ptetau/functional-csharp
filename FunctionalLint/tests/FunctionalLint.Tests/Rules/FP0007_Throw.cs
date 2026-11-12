using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0007_Throw
{
    [Fact]
    public Task Throw_statements_throw_expressions_and_rethrows_are_flagged() => Verify("""
        public static class C
        {
            public static int Length(string s) => s.Length > 0 ? s.Length : {|FP0007:throw|} new System.ArgumentException("empty");
            public static void Fail() { {|FP0007:throw|} new System.InvalidOperationException(); }
            public static void Again() { try { } catch { {|FP0007:throw|}; } }
        }
        """);

    [Fact]
    public Task Throw_helpers_are_flagged() => Verify("""
        using System;
        using System.Runtime.ExceptionServices;

        public static class C
        {
            public static int A(string? s) { {|FP0007:ArgumentNullException.ThrowIfNull(s)|}; return s.Length; }
            public static int B(int n) { {|FP0007:ArgumentOutOfRangeException.ThrowIfNegative(n)|}; return n; }
            public static void D(string s) => {|FP0007:ArgumentException.ThrowIfNullOrWhiteSpace(s)|};
            public static void E(bool disposed) => {|FP0007:ObjectDisposedException.ThrowIf(disposed, typeof(C))|};
            public static void F(Exception e) => {|FP0007:ExceptionDispatchInfo.Throw(e)|};
            public static void G(Exception e) => {|FP0007:ExceptionDispatchInfo.Capture(e).Throw()|};
        }
        """);

    [Fact]
    public Task Methods_that_only_look_like_throw_helpers_are_allowed() => Verify("""
        using System;
        using System.Runtime.ExceptionServices;

        public static class Guard
        {
            public static bool ThrowIfNull(object? o) => o is null;
        }

        public static class C
        {
            public static bool A(object? o) => Guard.ThrowIfNull(o);
            public static Exception B(Exception e) => ExceptionDispatchInfo.Capture(e).SourceException;
            public static string D(ArgumentException e) => e.Message;
        }
        """);

    [Fact]
    public Task Adapters_may_throw() => Verify($$"""
        {{AdapterAttribute}}

        [ImperativeAdapter]
        public static class Edge
        {
            public static void Fail() => throw new System.InvalidOperationException();
            public static void Check(string? s) => System.ArgumentNullException.ThrowIfNull(s);
        }
        """);

    [Fact]
    public Task Catching_without_throwing_is_allowed() => Verify("""
        public static class C
        {
            public static int Safe(System.Func<int> f) { try { return f(); } catch { return 0; } }
        }
        """);
}
