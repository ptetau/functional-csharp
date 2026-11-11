using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

/// <summary>FP0006 (call legacy code only through adapters) and FP0009 (adapters must not leak legacy types).</summary>
public class FP0006_FP0009_Legacy
{
    private const string Legacy = """
        namespace Legacy
        {
            public class OrderService
            {
                public static int Version = 1;
                private int _count;
                public int Next() { _count++; return _count; }
                public int Count => _count;
            }

            namespace Data { public class Db { public static Db Open() => new Db(); } }
        }
        """;

    private const string Config = "functional_lint.legacy_namespaces = Legacy";

    [Fact]
    public Task Legacy_code_only_through_adapters() => Verify($$"""
        {{Legacy}}

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
        """, Config);

    [Fact]
    public Task Fields_properties_method_groups_and_sub_namespaces_are_all_legacy() => Verify($$"""
        {{Legacy}}

        public static class Use
        {
            public static int V() => {|FP0006:Legacy.OrderService.Version|};
            public static int C(Legacy.OrderService s) => {|FP0006:s.Count|};
            public static System.Func<int> G(Legacy.OrderService s) => {|FP0006:s.Next|};
            public static object D() => {|FP0006:Legacy.Data.Db.Open()|};
        }
        """, Config);

    [Fact]
    public Task Several_legacy_namespaces_can_be_listed_with_spaces() => Verify("""
        namespace Old { public static class A { public static int X() => 1; } }
        namespace Older { public static class B { public static int Y() => 2; } }
        namespace OldStuff { public static class NotLegacy { public static int Z() => 3; } }

        public static class Use
        {
            public static int All() => {|FP0006:Old.A.X()|} + {|FP0006:Older.B.Y()|} + OldStuff.NotLegacy.Z();
        }
        """, "functional_lint.legacy_namespaces = Old ,  Older");

    [Fact]
    public Task Without_configuration_nothing_is_legacy() => Verify("""
        namespace Legacy { public static class A { public static int X() => 1; } }
        public static class Use { public static int Call() => Legacy.A.X(); }
        """);

    [Fact]
    public Task Legacy_code_itself_is_exempt_from_every_rule() => Verify($$"""
        {{Legacy}}
        """, Config);

    [Fact]
    public Task Adapters_may_not_expose_legacy_types_anywhere_public() => Verify($$"""
        {{Legacy}}

        {{AdapterAttribute}}

        [ImperativeAdapter]
        public sealed record Gateway(Legacy.OrderService {|FP0009:Service|})
        {
            private readonly Legacy.OrderService _hidden = Service;
            public Legacy.OrderService {|FP0009:Exposed|} = Service;
            public System.Threading.Tasks.Task<Legacy.OrderService> {|FP0009:Later|}() => null!;
            public int Take(Legacy.OrderService {|FP0009:other|}) => 0;
            internal Legacy.OrderService {|FP0009:Internal|} => _hidden;
            private Legacy.OrderService Private => _hidden;
        }
        """, Config);

    [Fact]
    public Task Adapter_constructors_may_take_legacy_services() => Verify($$"""
        {{Legacy}}

        {{AdapterAttribute}}

        [ImperativeAdapter]
        public sealed class Orders
        {
            private readonly Legacy.OrderService _service;
            public Orders(Legacy.OrderService service) { _service = service; }
            public int Next() => _service.Next();
        }
        """, Config);
}
