using System.Threading.Tasks;
using FunctionalLint.CodeFixes;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Fixes;

public class ReadOnlySignatureFixTests
{
    [Fact]
    public Task Mutable_collections_in_signatures_become_read_only_interfaces() => VerifyFix<ReadOnlySignatureFix>("""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public static class C
        {
            public static int Count(IList<int> {|FP0008:xs|}) => xs.Count;
            public static Task<Dictionary<string, int>> {|FP0008:Load|}() => null!;
            public static readonly ICollection<int> {|FP0008:Shared|} = null!;
            public static HashSet<int> {|FP0008:Ids|} => null!;
            public static int Keys(System.Collections.Generic.IDictionary<string, List<int>> {|FP0008:map|}) => map.Count;
        }

        public record R(List<int> {|FP0008:{|FP0014:Items|}|});
        """, """
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public static class C
        {
            public static int Count(IReadOnlyList<int> xs) => xs.Count;
            public static Task<IReadOnlyDictionary<string, int>> Load() => null!;
            public static readonly IReadOnlyCollection<int> Shared = null!;
            public static IReadOnlySet<int> Ids => null!;
            public static int Keys(System.Collections.Generic.IReadOnlyDictionary<string, IReadOnlyList<int>> map) => map.Count;
        }

        public record R(IReadOnlyList<int> {|FP0014:Items|});
        """);

    [Fact]
    public Task Types_without_a_read_only_interface_are_left_alone() => VerifyFix<ReadOnlySignatureFix>("""
        using System.Collections.Generic;

        public static class C
        {
            public static int Q(Queue<int> {|FP0008:q|}) => q.Count;
            public static int S(System.Text.StringBuilder {|FP0008:sb|}) => sb.Length;
        }
        """, """
        using System.Collections.Generic;

        public static class C
        {
            public static int Q(Queue<int> {|FP0008:q|}) => q.Count;
            public static int S(System.Text.StringBuilder {|FP0008:sb|}) => sb.Length;
        }
        """);
}

public class ReadOnlyLocalFixTests
{
    [Fact]
    public Task Locals_typed_as_mutable_interfaces_become_read_only() => VerifyFix<ReadOnlySignatureFix>("""
        using System.Collections.Generic;

        static class C
        {
            static int M()
            {
                {|FP0005:IList<int>|} a = [1, 2];
                {|FP0005:IDictionary<string, IList<int>>|} b = null!;
                var c = {|FP0005:new List<int>()|};
                return a.Count + b.Count + c.Count;
            }
        }
        """, """
        using System.Collections.Generic;

        static class C
        {
            static int M()
            {
                IReadOnlyList<int> a = [1, 2];
                IReadOnlyDictionary<string, IReadOnlyList<int>> b = null!;
                var c = {|FP0005:new List<int>()|};
                return a.Count + b.Count + c.Count;
            }
        }
        """);
}

public class ReadOnlySignatureFixSafetyTests
{
    [Fact]
    public Task Target_typed_new_is_left_alone() => VerifyFix<ReadOnlySignatureFix>("""
        using System.Collections.Generic;

        public static class C
        {
            private static readonly Dictionary<string, int> {|FP0008:Cache|} = {|FP0005:new()|};
        }
        """, """
        using System.Collections.Generic;

        public static class C
        {
            private static readonly Dictionary<string, int> {|FP0008:Cache|} = {|FP0005:new()|};
        }
        """);
}
