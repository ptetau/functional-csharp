using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0017_AdapterPlacement
{
    private const string Attribute = "public sealed class ImperativeAdapterAttribute : System.Attribute { }\n";

    [Fact]
    public Task Adapters_are_allowed_only_in_the_policy_namespaces() => Verify(Attribute + """
        namespace App.Adapters
        {
            [ImperativeAdapter] public static class Clock { public static System.DateTime Now() => System.DateTime.Now; }
        }

        namespace App.Core
        {
            [{|FP0017:ImperativeAdapter|}] public static class Clock { public static System.DateTime Now() => System.DateTime.Now; }

            public static class Prices
            {
                [{|FP0017:ImperativeAdapter|}] public static int Read() => 1;

                public static System.Func<int> Later() => [{|FP0017:ImperativeAdapter|}] () => 1;
            }
        }
        """, config: "functional_lint.adapter_namespaces = App.Adapters");

    [Fact]
    public Task Without_the_key_adapters_may_go_anywhere() => Verify(Attribute + """
        namespace App.Core
        {
            [ImperativeAdapter] public static class Clock { public static System.DateTime Now() => System.DateTime.Now; }
        }
        """);

    [Fact]
    public async Task None_means_no_adapters_in_this_project()
    {
        var findings = await Diagnose(Attribute + """
            namespace App.Core
            {
                [ImperativeAdapter] public static class Clock { public static System.DateTime Now() => System.DateTime.Now; }
            }
            """, "functional_lint.adapter_namespaces = none");

        Assert.Equal(
            "'Clock' is an [ImperativeAdapter], and this project allows none. Move it to a shell project, or pass the value in.",
            Assert.Single(findings, f => f.Id == "FP0017").Message);
    }
}
