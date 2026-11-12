using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FunctionalLint.CodeFixes;

/// <summary>
/// The types whose instances some code in a solution may compare or hash. A class compares by reference and a record
/// by value, so turning a class into a record changes what this code does: two equal tags in `Distinct()` become one.
/// It errs towards "compared": any of these counts, wherever the type appears in it.
/// <list type="bullet">
/// <item>`a == b` and `a != b`, except against `null` (a record's `==` gives the same answer there).</item>
/// <item>`Equals` and `GetHashCode`, and test assertions such as `Assert.Equal`.</item>
/// <item>LINQ and collection methods that compare items or keys: `Distinct`, `Contains`, `IndexOf`, `Except`,
/// and the key of `GroupBy`, `ToDictionary`, `Join`...</item>
/// <item>The key or item of a dictionary, set, lookup or equality comparer: `HashSet&lt;Tag&gt;`.</item>
/// <item>A record, struct, tuple or anonymous type holding the type, when that is compared in any of these ways.</item>
/// </list>
/// </summary>
internal static class EqualityUses
{
    // Computed once per solution snapshot: heal asks for every class in a project against the same solution.
    private static readonly ConditionalWeakTable<Solution, Task<ImmutableHashSet<string>>> Cache = new();

    /// <summary>True if code in the solution may compare or hash instances of the type, or of an interface it implements.</summary>
    public static async Task<bool> ComparedAsync(Solution solution, INamedTypeSymbol type, CancellationToken ct)
    {
        var compared = await Cache.GetValue(solution, s => Task.Run(() => FindAsync(s))).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return new[] { type }.Concat(type.AllInterfaces).Any(t => compared.Contains(Key(t)));
    }

    // Methods that compare their items (TSource, T) or, when they have one, their keys (TKey).
    private static readonly ImmutableHashSet<string> ComparingMethods = ImmutableHashSet.Create(
        "Distinct", "DistinctBy", "GroupBy", "GroupJoin", "Join", "ToLookup", "ToHashSet", "ToImmutableHashSet", "ToFrozenSet",
        "ToDictionary", "ToImmutableDictionary", "ToFrozenDictionary", "Contains", "IndexOf", "LastIndexOf", "Remove", "Replace",
        "Except", "ExceptBy", "Intersect", "IntersectBy", "Union", "UnionBy", "SequenceEqual", "CountBy", "AggregateBy",
        "Combine", "Add", "Equal", "NotEqual", "AreEqual", "AreNotEqual", "DoesNotContain");

    // Instance methods of a list that compare its items, whatever the list is (ImmutableArray, ImmutableList...).
    private static readonly ImmutableHashSet<string> ComparingListMethods = ImmutableHashSet.Create(
        "Contains", "IndexOf", "LastIndexOf", "Remove", "Replace");

    // Methods whose receiver and arguments are compared.
    private static readonly ImmutableHashSet<string> ComparingCalls = ImmutableHashSet.Create(
        "Equals", "GetHashCode", "Equal", "NotEqual", "AreEqual", "AreNotEqual");

    // Types that hash or compare their first type argument: the key of a dictionary, the item of a set.
    private static readonly ImmutableHashSet<string> HashingTypes = ImmutableHashSet.Create(
        "Dictionary", "HashSet", "ImmutableDictionary", "ImmutableHashSet", "ConcurrentDictionary", "FrozenDictionary",
        "FrozenSet", "ILookup", "Lookup", "IGrouping", "ISet", "IReadOnlySet", "IDictionary", "IReadOnlyDictionary",
        "IImmutableSet", "IImmutableDictionary", "IEqualityComparer", "EqualityComparer", "OrderedDictionary");

    private static async Task<ImmutableHashSet<string>> FindAsync(Solution solution)
    {
        var compared = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var project in solution.Projects.Where(p => p.Language == LanguageNames.CSharp))
        {
            if (await project.GetCompilationAsync().ConfigureAwait(false) is not { } compilation) continue;
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync().ConfigureAwait(false);
                foreach (var node in root.DescendantNodes())
                    foreach (var type in ComparedAt(node, model))
                        Add(compared, type);
            }
        }
        return compared.Select(Key).ToImmutableHashSet();
    }

    private static IEnumerable<ITypeSymbol?> ComparedAt(SyntaxNode node, SemanticModel model)
    {
        switch (node)
        {
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression):
                if (IsNull(binary.Left) || IsNull(binary.Right)) break;
                yield return model.GetTypeInfo(binary.Left).Type;
                yield return model.GetTypeInfo(binary.Right).Type;
                break;

            case InvocationExpressionSyntax invocation when Name(invocation) is { } name
                && (ComparingMethods.Contains(name) || ComparingCalls.Contains(name) || name is "Create" or "CreateRange"):
                var symbol = model.GetSymbolInfo(invocation).Symbol ?? model.GetSymbolInfo(invocation).CandidateSymbols.FirstOrDefault();
                if (symbol is not IMethodSymbol method) break;

                if (ComparingMethods.Contains(name))
                {
                    var key = method.TypeParameters.Select((p, i) => (p, i)).FirstOrDefault(x => x.p.Name == "TKey");
                    foreach (var argument in key.p is null ? method.TypeArguments : ImmutableArray.Create(method.TypeArguments[key.i]))
                        yield return argument;
                }
                if (ComparingListMethods.Contains(name) && !method.IsStatic)
                    foreach (var argument in method.ContainingType.TypeArguments)
                        yield return argument;
                if (ComparingCalls.Contains(name))
                {
                    if (invocation.Expression is MemberAccessExpressionSyntax access && !method.IsStatic)
                        yield return model.GetTypeInfo(access.Expression).Type;
                    foreach (var argument in invocation.ArgumentList.Arguments)
                        yield return model.GetTypeInfo(argument.Expression).Type;
                }
                if (HashedArgument(method.ReturnType) is { } hashed) yield return hashed;
                break;

            case GenericNameSyntax generic when HashingTypes.Contains(generic.Identifier.ValueText):
                if (model.GetSymbolInfo(generic).Symbol is INamedTypeSymbol named) yield return HashedArgument(named);
                break;
        }
    }

    // `Dictionary<Tag, int>` hashes Tag.
    private static ITypeSymbol? HashedArgument(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true } named && HashingTypes.Contains(named.Name)
        && named.ContainingNamespace?.ToDisplayString() is { } ns && ns.StartsWith("System", System.StringComparison.Ordinal)
            ? named.TypeArguments[0]
            : null;

    // A compared type, and what comparing it compares: type arguments and elements, and the members of records,
    // structs, tuples and anonymous types (their equality compares each member).
    private static void Add(HashSet<ITypeSymbol> compared, ITypeSymbol? type)
    {
        if (type is null || type.TypeKind == TypeKind.Error || !compared.Add(type)) return;
        if (type is IArrayTypeSymbol array) Add(compared, array.ElementType);
        if (type is not INamedTypeSymbol named) return;

        foreach (var argument in named.TypeArguments) Add(compared, argument);
        if (ComparesMembers(named))
            foreach (var member in named.GetMembers().Where(m => !m.IsStatic))
                Add(compared, member switch
                {
                    IFieldSymbol field => field.Type,
                    IPropertySymbol { IsIndexer: false } property => property.Type,
                    _ => null,
                });
    }

    // Records, anonymous types and tuples compare member by member, and so do structs that keep ValueType.Equals.
    private static bool ComparesMembers(INamedTypeSymbol type) =>
        type.IsRecord || type.IsAnonymousType || type.IsTupleType
        || type.TypeKind == TypeKind.Struct && type.SpecialType == SpecialType.None
           && !type.GetMembers(nameof(Equals)).OfType<IMethodSymbol>().Any(m => m.IsOverride && m.Parameters.Length == 1);

    private static string? Name(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => null,
    };

    private static bool IsNull(ExpressionSyntax expression) => expression.IsKind(SyntaxKind.NullLiteralExpression)
        || expression is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } bang && IsNull(bang.Operand);

    private static string Key(ITypeSymbol type) => type.OriginalDefinition.ToDisplayString();
}
