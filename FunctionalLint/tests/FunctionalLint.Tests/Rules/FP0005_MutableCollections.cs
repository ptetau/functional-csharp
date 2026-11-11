using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0005_MutableCollections
{
    [Fact]
    public Task Creating_mutable_collections_is_flagged() => Verify("""
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Text;

        static class C
        {
            static void M()
            {
                var a = {|FP0005:new List<int>()|};
                List<int> b = {|FP0005:new()|};
                List<int> c = {|FP0005:[1, 2]|};
                var d = {|FP0005:new Dictionary<string, int>()|};
                var e = {|FP0005:new HashSet<int>()|};
                var f = {|FP0005:new Queue<int>()|};
                var g = {|FP0005:new Stack<int>()|};
                var h = {|FP0005:new ConcurrentDictionary<int, int>()|};
                var i = {|FP0005:new StringBuilder()|};
            }
        }
        """);

    [Fact]
    public Task Linq_methods_that_build_mutable_collections_are_flagged() => Verify("""
        using System.Linq;

        static class C
        {
            static void M(int[] xs)
            {
                var a = {|FP0005:xs.ToList()|};
                var b = {|FP0005:xs.ToDictionary(x => x)|};
                var c = {|FP0005:xs.ToHashSet()|};
            }
        }
        """);

    [Fact]
    public Task Immutable_collections_and_arrays_are_allowed() => Verify("""
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;

        static class C
        {
            static void M(int[] xs)
            {
                ImmutableArray<int> a = [1, 2];
                var b = ImmutableDictionary<string, int>.Empty.Add("a", 1);
                var c = xs.ToImmutableArray();
                var d = xs.Select(x => x * 2).ToArray();
                int[] e = [1, 2];
                IReadOnlyList<int> f = [1, 2];
                var g = xs.ToImmutableDictionary(x => x);
                var h = $"{a.Length}{b.Count}";
            }
        }
        """);

    [Fact]
    public Task Calling_your_own_method_is_reported_once_on_its_signature_not_at_each_call() => Verify("""
        using System.Collections.Generic;

        static class C
        {
            static List<int> {|FP0008:Make|}() => {|FP0005:new List<int>()|};
            static int Use() => Make().Count;
        }
        """);
}
