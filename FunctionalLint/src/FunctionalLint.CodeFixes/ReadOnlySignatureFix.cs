using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FunctionalLint.CodeFixes;

/// <summary>
/// FP0008, and FP0005 on a local's declared type: swaps a mutable collection type for its read-only interface,
/// e.g. IList&lt;T&gt; and List&lt;T&gt; become IReadOnlyList&lt;T&gt;. Callers passing a list still compile.
/// Code that went on to mutate the collection stops compiling, which shows where it happened.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReadOnlySignatureFix)), Shared]
public sealed class ReadOnlySignatureFix : CodeFixProvider
{
    // Mutable type (metadata name) -> read-only interface. All live in System.Collections.Generic,
    // so swapping the simple name keeps whatever using or qualification the code already had.
    private static string? ReadOnlyFor(string metadataName) => metadataName switch
    {
        "System.Collections.Generic.IList`1" or "System.Collections.Generic.List`1" => "IReadOnlyList",
        "System.Collections.Generic.ICollection`1" => "IReadOnlyCollection",
        "System.Collections.Generic.IDictionary`2" or "System.Collections.Generic.Dictionary`2" => "IReadOnlyDictionary",
        "System.Collections.Generic.ISet`1" or "System.Collections.Generic.HashSet`1" => "IReadOnlySet",
        _ => null,
    };

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(FunctionalAnalyzer.MutableSignature.Id, FunctionalAnalyzer.NoMutableTypes.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var found = await FixHelpers.TokenAt(context.Document, context.Span, context.CancellationToken).ConfigureAwait(false);
        if (found is not var (root, token)) return;
        var diagnostic = context.Diagnostics[0];

        // FP0005 is only fixable when it points at a local's declared type, not at `new List<int>()`.
        var typeSyntax = diagnostic.Id == FunctionalAnalyzer.NoMutableTypes.Id
            ? token.Parent?.FirstAncestorOrSelf<VariableDeclarationSyntax>() is { } local && local.Type.Span == context.Span ? local.Type : null
            : DeclaredType(token.Parent);
        if (typeSyntax is null || HasTargetTypedNew(typeSyntax.Parent)) return;

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null) return;

        // Swap the outermost mutable type, and an inner one only where every enclosing generic is covariant there.
        // `Dictionary<string, List<int>>` becomes `IReadOnlyDictionary<string, List<int>>`: swapping the inner List
        // too would give IReadOnlyDictionary<string, IReadOnlyList<int>>, which a Dictionary<string, List<int>>
        // doesn't convert to, because a dictionary's value type is invariant. `IReadOnlyList<IList<int>>` can become
        // `IReadOnlyList<IReadOnlyList<int>>`, because IReadOnlyList<out T> is covariant.
        var candidates = typeSyntax.DescendantNodesAndSelf().OfType<GenericNameSyntax>()
            .Select(name => (Name: name, Replacement: Replacement(model, name)))
            .Where(s => s.Replacement is not null)
            .ToImmutableArray();
        var swaps = candidates
            .Where(s => !candidates.Any(outer => outer.Name != s.Name && outer.Name.Span.Contains(s.Name.Span)))
            .Where(s => OnlyInCovariantPositions(model, s.Name, typeSyntax))
            .ToImmutableArray();
        if (swaps.IsEmpty) return;

        var title = $"Use {swaps[0].Replacement}<...>";
        context.RegisterCodeFix(CodeAction.Create(title, _ =>
        {
            // `rewritten` already carries any swaps nested inside this name, e.g. IDictionary<string, List<int>>.
            var newType = typeSyntax.ReplaceNodes(swaps.Select(s => s.Name), (original, rewritten) =>
                rewritten.WithIdentifier(SyntaxFactory.Identifier(swaps.FirstOrDefault(s => s.Name == original).Replacement!)
                    .WithTriviaFrom(original.Identifier)));
            return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(typeSyntax, newType)));
        }, equivalenceKey: diagnostic.Id), diagnostic);
    }

    private static string? Replacement(SemanticModel model, GenericNameSyntax name)
    {
        if (model.GetSymbolInfo(name).Symbol is not INamedTypeSymbol type) return null;
        var original = type.OriginalDefinition;
        var metadataName = $"{original.ContainingNamespace.ToDisplayString()}.{original.MetadataName}";
        if (ReadOnlyFor(metadataName) is not { } replacement) return null;

        // IReadOnlySet<T> only exists from .NET 5.
        var arity = original.Arity;
        return model.Compilation.GetTypeByMetadataName($"System.Collections.Generic.{replacement}`{arity}") is null
            ? null
            : replacement;
    }

    // Walks up from a mutable type to the declared type. Only steps that keep a conversion valid are allowed:
    // a type argument whose parameter is covariant (`out T`), a qualified name, a nullable or array type. Anything
    // else (a tuple element, an invariant type argument) would stop the old type converting to the new one.
    private static bool OnlyInCovariantPositions(SemanticModel model, SyntaxNode node, SyntaxNode outermost) =>
        node == outermost || node.Parent switch
        {
            QualifiedNameSyntax or AliasQualifiedNameSyntax or NullableTypeSyntax or ArrayTypeSyntax =>
                OnlyInCovariantPositions(model, node.Parent!, outermost),
            // Step over the argument list to the generic type that holds it, if this argument is covariant there.
            TypeArgumentListSyntax { Parent: GenericNameSyntax generic } arguments =>
                model.GetSymbolInfo(generic).Symbol is INamedTypeSymbol type
                && arguments.Arguments.IndexOf((TypeSyntax)node) is var index && index >= 0
                && (type.OriginalDefinition.TypeParameters[index].Variance == VarianceKind.Out || IsAsyncResult(generic, type, outermost))
                && OnlyInCovariantPositions(model, generic, outermost),
            _ => false,
        };

    // `async Task<List<int>> F()`: the body returns the list itself, so `Task<IReadOnlyList<int>>` still compiles.
    private static bool IsAsyncResult(GenericNameSyntax generic, INamedTypeSymbol type, SyntaxNode outermost) =>
        generic == outermost
        && type.OriginalDefinition.ToDisplayString() is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>"
        && outermost.Parent switch
        {
            MethodDeclarationSyntax m => m.ReturnType == outermost && m.Modifiers.Any(SyntaxKind.AsyncKeyword),
            LocalFunctionStatementSyntax l => l.ReturnType == outermost && l.Modifiers.Any(SyntaxKind.AsyncKeyword),
            _ => false,
        };

    // `IReadOnlyList<int> xs = new();` can't compile: there's no interface to construct. Leave those to a person.
    private static bool HasTargetTypedNew(SyntaxNode? declaration) => declaration switch
    {
        VariableDeclarationSyntax v => v.Variables.Any(x => x.Initializer?.Value is ImplicitObjectCreationExpressionSyntax),
        PropertyDeclarationSyntax p => p.Initializer?.Value is ImplicitObjectCreationExpressionSyntax,
        _ => false,
    };

    // The type written in the declaration the diagnostic points at.
    private static TypeSyntax? DeclaredType(SyntaxNode? node) => node switch
    {
        ParameterSyntax p => p.Type,
        MethodDeclarationSyntax m => m.ReturnType,
        OperatorDeclarationSyntax o => o.ReturnType,
        BasePropertyDeclarationSyntax p => p.Type,
        VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax v } => v.Type,
        _ => null,
    };
}
