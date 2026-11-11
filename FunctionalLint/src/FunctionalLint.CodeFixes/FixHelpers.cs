using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace FunctionalLint.CodeFixes;

internal static class FixHelpers
{
    // The token a diagnostic points at (an identifier, a keyword...).
    public static async Task<(SyntaxNode Root, SyntaxToken Token)?> TokenAt(
        Document document, Microsoft.CodeAnalysis.Text.TextSpan span, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return null;
        return (root, root.FindToken(span.Start));
    }

    public static Document Replace(Document document, SyntaxNode root, SyntaxNode oldNode, SyntaxNode newNode) =>
        document.WithSyntaxRoot(root.ReplaceNode(oldNode, newNode.WithAdditionalAnnotations(Formatter.Annotation)));

    // Adds a modifier after access/static/new modifiers, keeping the declaration's leading trivia in place.
    public static SyntaxTokenList AddModifier(SyntaxTokenList modifiers, SyntaxKind kind)
    {
        var token = SyntaxFactory.Token(kind).WithTrailingTrivia(SyntaxFactory.Space);
        var insertAt = modifiers.Count(m => m.Kind() is SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword
            or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword or SyntaxKind.FileKeyword
            or SyntaxKind.StaticKeyword or SyntaxKind.NewKeyword);
        return modifiers.Insert(insertAt, token);
    }

    // Puts a modifier list on a declaration that may have none yet, moving its leading trivia onto the first modifier.
    public static T WithModifier<T>(T node, SyntaxTokenList modifiers, SyntaxKind kind,
        System.Func<T, SyntaxTokenList, T> withModifiers) where T : SyntaxNode
    {
        if (modifiers.Count > 0) return withModifiers(node, AddModifier(modifiers, kind));

        var leading = node.GetLeadingTrivia();
        var bare = node.WithoutLeadingTrivia();
        var token = SyntaxFactory.Token(kind).WithLeadingTrivia(leading).WithTrailingTrivia(SyntaxFactory.Space);
        return withModifiers(bare, SyntaxFactory.TokenList(token));
    }
}
