using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FunctionalLint.Adapters;

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
[ImperativeAdapter]
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
        var compilations = await Task.WhenAll(solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .Select(p => p.GetCompilationAsync())).ConfigureAwait(false);
        var found = compilations.OfType<Compilation>().SelectMany(compilation => compilation.SyntaxTrees.SelectMany(tree =>
        {
            var model = compilation.GetSemanticModel(tree);
            return tree.GetRoot().DescendantNodes().SelectMany(node => ComparedAt(node, model));
        }));
        return found.Aggregate(ImmutableHashSet.Create<ITypeSymbol>(SymbolEqualityComparer.Default), Add)
            .Select(Key).ToImmutableHashSet();
    }

    private static IEnumerable<ITypeSymbol?> ComparedAt(SyntaxNode node, SemanticModel model) => node switch
    {
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression) =>
            IsNull(binary.Left) || IsNull(binary.Right)
                ? []
                : [model.GetTypeInfo(binary.Left).Type, model.GetTypeInfo(binary.Right).Type],

        InvocationExpressionSyntax invocation when Name(invocation) is { } name
            && (ComparingMethods.Contains(name) || ComparingCalls.Contains(name) || name is "Create" or "CreateRange") =>
            (model.GetSymbolInfo(invocation).Symbol ?? model.GetSymbolInfo(invocation).CandidateSymbols.FirstOrDefault()) is IMethodSymbol method
                ? ComparedBy(invocation, name, method, model)
                : [],

        GenericNameSyntax generic when HashingTypes.Contains(generic.Identifier.ValueText) =>
            model.GetSymbolInfo(generic).Symbol is INamedTypeSymbol named ? [HashedArgument(named)] : [],

        _ => [],
    };

    // What a call compares: its items or keys, a list's items, the receiver and arguments of Equals and assertions,
    // and the key or item of a dictionary or set it returns.
    private static IEnumerable<ITypeSymbol?> ComparedBy(InvocationExpressionSyntax invocation, string name, IMethodSymbol method, SemanticModel model)
    {
        IEnumerable<ITypeSymbol?> items = !ComparingMethods.Contains(name) ? []
            : method.TypeParameters.FirstOrDefault(p => p.Name == "TKey") is { } key ? [method.TypeArguments[key.Ordinal]]
            : method.TypeArguments;
        IEnumerable<ITypeSymbol?> listItems = ComparingListMethods.Contains(name) && !method.IsStatic ? method.ContainingType.TypeArguments : [];
        IEnumerable<ITypeSymbol?> receiver = ComparingCalls.Contains(name) && invocation.Expression is MemberAccessExpressionSyntax access && !method.IsStatic
            ? [model.GetTypeInfo(access.Expression).Type]
            : [];
        IEnumerable<ITypeSymbol?> arguments = ComparingCalls.Contains(name)
            ? invocation.ArgumentList.Arguments.Select(argument => model.GetTypeInfo(argument.Expression).Type)
            : [];
        IEnumerable<ITypeSymbol?> hashed = HashedArgument(method.ReturnType) is { } type ? [type] : [];
        return items.Concat(listItems).Concat(receiver).Concat(arguments).Concat(hashed);
    }

    // `Dictionary<Tag, int>` hashes Tag.
    private static ITypeSymbol? HashedArgument(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true } named && HashingTypes.Contains(named.Name)
        && named.ContainingNamespace?.ToDisplayString() is { } ns && ns.StartsWith("System", System.StringComparison.Ordinal)
            ? named.TypeArguments[0]
            : null;

    // A compared type, and what comparing it compares: type arguments and elements, and the members of records,
    // structs, tuples and anonymous types (their equality compares each member).
    private static ImmutableHashSet<ITypeSymbol> Add(ImmutableHashSet<ITypeSymbol> compared, ITypeSymbol? type) =>
        type is null || type.TypeKind == TypeKind.Error || compared.Contains(type)
            ? compared
            : Parts(type).Aggregate(compared.Add(type), Add);

    private static IEnumerable<ITypeSymbol?> Parts(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => [array.ElementType],
        INamedTypeSymbol named => named.TypeArguments.Concat(ComparesMembers(named)
            ? named.GetMembers().Where(m => !m.IsStatic).Select(member => member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol { IsIndexer: false } property => property.Type,
                _ => null,
            })
            : []),
        _ => [],
    };

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
