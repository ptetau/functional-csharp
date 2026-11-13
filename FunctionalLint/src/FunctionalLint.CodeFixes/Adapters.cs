using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace FunctionalLint.Adapters;

// The code fixes' imperative part: talking to the editor's workspace, which is asynchronous. FunctionalLint checks
// the code fixes with its own rules (functionallint.policy), and this namespace is where adapters may live.

/// <summary>Marks what the rules skip. Matched by name, like the copy a consuming project defines.</summary>
[AttributeUsage(AttributeTargets.All)]
internal sealed class ImperativeAdapterAttribute : Attribute;

[ImperativeAdapter]
internal static class Documents
{
    /// <summary>The document's syntax root and the token a diagnostic points at (an identifier, a keyword...).</summary>
    public static async Task<(SyntaxNode Root, SyntaxToken Token)?> TokenAt(
        Document document, Microsoft.CodeAnalysis.Text.TextSpan span, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        return root is null ? null : (root, root.FindToken(span.Start));
    }
}
