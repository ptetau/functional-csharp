using System.Threading.Tasks;
using FunctionalLint.CodeFixes;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Fixes;

public class AsyncVoidFixTests
{
    [Fact]
    public Task Async_void_methods_and_local_functions_return_Task() => VerifyFix<AsyncVoidFix>("""
        using System.Threading.Tasks;

        public static class C
        {
            public static async void {|FP0010:Fire|}() => await Task.Yield();

            public static void Outer()
            {
                async void {|FP0010:Local|}() => await Task.Yield();
                Local();
            }
        }
        """, """
        using System.Threading.Tasks;

        public static class C
        {
            public static async Task Fire() => await Task.Yield();

            public static void Outer()
            {
                async Task Local() => await Task.Yield();
                Local();
            }
        }
        """);

    [Fact]
    public Task The_Tasks_namespace_is_imported_when_missing() => VerifyFix<AsyncVoidFix>("""
        public static class C
        {
            public static async void {|FP0010:Fire|}() => await System.Threading.Tasks.Task.Yield();
        }
        """, """
        using System.Threading.Tasks;

        public static class C
        {
            public static async Task Fire() => await System.Threading.Tasks.Task.Yield();
        }
        """);

    [Fact]
    public Task Lambdas_and_overrides_are_left_alone() => VerifyFix<AsyncVoidFix>("""
        using System;
        using System.Threading.Tasks;

        public abstract record Handler { public abstract void Handle(); }

        public sealed record Impl : Handler
        {
            public override async void {|FP0010:Handle|}() => await Task.Yield();
            public static Action Make() => {|FP0010:async|} () => await Task.Yield();
        }
        """, """
        using System;
        using System.Threading.Tasks;

        public abstract record Handler { public abstract void Handle(); }

        public sealed record Impl : Handler
        {
            public override async void {|FP0010:Handle|}() => await Task.Yield();
            public static Action Make() => {|FP0010:async|} () => await Task.Yield();
        }
        """);
}
