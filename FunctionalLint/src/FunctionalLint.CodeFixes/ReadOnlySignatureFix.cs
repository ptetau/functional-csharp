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
    private static readonly Dictionary<string, string> ReadOnlyFor = new()
    {
        ["System.Collections.Generic.IList`1"] = "IReadOnlyList",
        ["System.Collections.Generic.List`1"] = "IReadOnlyList",
        ["System.Collections.Generic.ICollection`1"] = "IReadOnlyCollection",
        ["System.Collections.Generic.IDictionary`2"] = "IReadOnlyDictionary",
        ["System.Collections.Generic.Dictionary`2"] = "IReadOnlyDictionary",
        ["System.Collections.Generic.ISet`1"] = "IReadOnlySet",
        ["System.Collections.Generic.HashSet`1"] = "IReadOnlySet",
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

        var swaps = typeSyntax.DescendantNodesAndSelf().OfType<GenericNameSyntax>()
            .Select(name => (Name: name, Replacement: Replacement(model, name)))
            .Where(s => s.Replacement is not null)
            .ToList();
        if (swaps.Count == 0) return;

        var title = $"Use {swaps[0].Replacement}<...>";
        context.RegisterCodeFix(CodeAction.Create(title, _ =>
        {
            // `rewritten` already carries any swaps nested inside this name, e.g. IDictionary<string, List<int>>.
            var newType = typeSyntax.ReplaceNodes(swaps.Select(s => s.Name), (original, rewritten) =>
                rewritten.WithIdentifier(SyntaxFactory.Identifier(swaps.First(s => s.Name == original).Replacement!)
                    .WithTriviaFrom(original.Identifier)));
            return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(typeSyntax, newType)));
        }, equivalenceKey: diagnostic.Id), diagnostic);
    }

    private static string? Replacement(SemanticModel model, GenericNameSyntax name)
    {
        if (model.GetSymbolInfo(name).Symbol is not INamedTypeSymbol type) return null;
        var original = type.OriginalDefinition;
        var metadataName = $"{original.ContainingNamespace.ToDisplayString()}.{original.MetadataName}";
        if (!ReadOnlyFor.TryGetValue(metadataName, out var replacement)) return null;

        // IReadOnlySet<T> only exists from .NET 5.
        var arity = original.Arity;
        return model.Compilation.GetTypeByMetadataName($"System.Collections.Generic.{replacement}`{arity}") is null
            ? null
            : replacement;
    }

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
