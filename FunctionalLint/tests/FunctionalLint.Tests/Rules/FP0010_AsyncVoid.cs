using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0010_AsyncVoid
{
    [Fact]
    public Task Async_void_methods_local_functions_and_lambdas_are_flagged() => Verify("""
        using System;
        using System.Threading.Tasks;

        public static class C
        {
            public static async void {|FP0010:Fire|}() => await Task.Yield();
            public static Action Make() => {|FP0010:async|} () => await Task.Yield();
            public static void Outer()
            {
                async void {|FP0010:Local|}() => await Task.Yield();
                Local();
            }
        }
        """);

    [Fact]
    public Task Async_Task_is_allowed() => Verify("""
        using System;
        using System.Threading.Tasks;

        public static class C
        {
            public static async Task<int> Later() { await Task.Yield(); return 1; }
            public static Func<Task> Make() => async () => await Task.Yield();
        }
        """);

    [Fact]
    public Task Adapters_may_use_async_void_for_event_handlers() => Verify($$"""
        using System.Threading.Tasks;

        {{AdapterAttribute}}

        [ImperativeAdapter]
        public static class Ui
        {
            public static async void OnClick(object sender, System.EventArgs e) => await Task.Yield();
        }
        """);
}
