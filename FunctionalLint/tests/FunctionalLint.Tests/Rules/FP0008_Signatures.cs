using System.Threading.Tasks;
using Xunit;
using static FunctionalLint.Tests.Lint;

namespace FunctionalLint.Tests.Rules;

public class FP0008_Signatures
{
    [Fact]
    public Task Mutable_types_in_parameters_returns_fields_and_properties_are_flagged() => Verify("""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        public static class C
        {
            public static int Count(IList<int> {|FP0008:xs|}) => xs.Count;
            public static Task<Dictionary<string, int>> {|FP0008:Load|}() => null!;
            public static readonly ICollection<int> {|FP0008:Shared|} = null!;
            public static ISet<int> {|FP0008:Ids|} => null!;
            public static int Deep(System.Func<List<int>> {|FP0008:make|}) => 0;
            public static int Arr(List<int>[] {|FP0008:lists|}) => lists.Length;
        }
        """);

    [Fact]
    public Task Positional_record_parameters_are_reported_once() => Verify("""
        using System.Collections.Generic;

        public record R(List<int> {|FP0008:Xs|});
        """);

    [Fact]
    public Task Interfaces_and_indexers_are_checked() => Verify("""
        using System.Collections.Generic;

        public interface IRepo
        {
            IList<int> {|FP0008:All|}();
            int this[List<int> {|FP0008:key|}] { get; }
        }
        """);

    [Fact]
    public Task Overrides_and_explicit_implementations_cannot_choose_their_signature() => Verify("""
        using System.Collections.Generic;

        public interface IRepo { IList<int> {|FP0008:All|}(); }

        public abstract record Base { public abstract IList<int> {|FP0008:Items|}(); }

        public sealed record Impl : Base, IRepo
        {
            IList<int> IRepo.All() => null!;
            public override IList<int> Items() => null!;
        }
        """);

    [Fact]
    public Task Read_only_and_immutable_types_and_arrays_are_allowed() => Verify("""
        using System.Collections.Generic;
        using System.Collections.Immutable;

        public static class C
        {
            public static int A(IReadOnlyList<int> xs, IEnumerable<int> ys, ImmutableArray<int> zs, int[] arr) => 0;
            public static IReadOnlyDictionary<string, int> B() => ImmutableDictionary<string, int>.Empty;
        }
        """);
}
