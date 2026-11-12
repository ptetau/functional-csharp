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
                var b = {|FP0005:{|FP0015:xs.ToDictionary(x => x)|}|};
                var c = {|FP0005:xs.ToHashSet()|};
            }
        }
        """);

    [Fact]
    public Task Locals_typed_as_mutable_interfaces_are_flagged() => Verify("""
        using System.Collections.Generic;

        static class C
        {
            static int M(IReadOnlyList<int> source)
            {
                {|FP0005:IList<int>|} a = [1, 2];
                {|FP0005:ICollection<int>|} b = [3];
                {|FP0005:IDictionary<string, int>|} c = {|FP0005:new Dictionary<string, int>()|};
                {|FP0005:System.Func<ISet<int>>|} d = () => null!;
                IReadOnlyList<int> e = source;
                return a.Count + b.Count + c.Count + e.Count;
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
                var b = ImmutableDictionary<string, int>.Empty.SetItem("a", 1);
                var c = xs.ToImmutableArray();
                var d = xs.Select(x => x * 2).ToArray();
                int[] e = [1, 2];
                IReadOnlyList<int> f = [1, 2];
                var g = xs.GroupBy(x => x).ToImmutableDictionary(group => group.Key, group => group.Count());
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

public class FP0005_MoreMutableTypes
{
    [Fact]
    public Task Builders_object_model_and_other_collections_are_flagged() => Verify("""
        using System.Collections;
        using System.Collections.Concurrent;
        using System.Collections.Immutable;
        using System.Collections.ObjectModel;
        using System.IO;

        static class C
        {
            static void M(ImmutableArray<int> xs)
            {
                var a = {|FP0005:ImmutableArray.CreateBuilder<int>()|};
                var b = {|FP0005:xs.ToBuilder()|};
                var c = {|FP0005:new Collection<int>()|};
                var d = {|FP0005:new ArrayList()|};
                var e = {|FP0005:new ConcurrentQueue<int>()|};
                var f = {|FP0005:new MemoryStream()|};
                var g = new ReadOnlyCollection<int>(xs);
            }
        }
        """);

    [Fact]
    public Task A_builder_is_reported_where_it_is_made_not_at_every_call() => Verify("""
        static class C
        {
            static string M() => {|FP0005:new System.Text.StringBuilder()|}.Append("a").Append("b").ToString();
        }
        """);
}

public class FP0005_PriorityQueue
{
    [Fact]
    public Task Priority_queues_are_mutable() => Verify("""
        static class C
        {
            static object M() => {|FP0005:new System.Collections.Generic.PriorityQueue<int, int>()|};
        }
        """);
}
