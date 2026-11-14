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
/// FP0011, line endings only: adds <c>NewLine = "\n"</c> after <c>WriteIndented = true</c> (or <c>NewLineChars</c> for
/// XmlWriterSettings), and passes <c>"\n"</c> to <c>ReplaceLineEndings()</c>, so the output is the same on every OS.
/// Other FP0011 findings get no fix: where a value should come from is a design choice.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NewLineFix)), Shared]
public sealed class NewLineFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(FunctionalAnalyzer.ImpureApi.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var addProperty = diagnostic.Properties.TryGetValue(FunctionalAnalyzer.AddNewLineKey, out var name) ? name : null;
        var addArgument = diagnostic.Properties.ContainsKey(FunctionalAnalyzer.AddArgumentKey);
        if (addProperty is null && !addArgument) return;

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindNode(context.Span);
        if (root is null || node is null) return;

        if (addArgument)
        {
            if (node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault() is not { } call
                || call.ArgumentList.Arguments.Count != 0) return;
            context.RegisterCodeFix(CodeAction.Create("Pass \"\\n\"",
                _ => Task.FromResult(FixHelpers.Replace(context.Document, root, call.ArgumentList,
                    call.ArgumentList.AddArguments(SyntaxFactory.Argument(NewLineLiteral())))),
                equivalenceKey: "FP0011.NewLine"), diagnostic);
            return;
        }

        if (addProperty is null
            || node.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault() is not { } indent
            || indent.Parent is not InitializerExpressionSyntax initializer) return;

        context.RegisterCodeFix(CodeAction.Create($"Set {addProperty} = \"\\n\"",
            _ => Task.FromResult(FixHelpers.Replace(context.Document, root, initializer, WithNewLine(initializer, indent, addProperty))),
            equivalenceKey: "FP0011.NewLine"), diagnostic);
    }

    private static LiteralExpressionSyntax NewLineLiteral() =>
        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("\"\\n\"", "\n"));

    // Inserts the assignment right after `indent`, laid out like its neighbours: same indentation, same separator.
    private static InitializerExpressionSyntax WithNewLine(InitializerExpressionSyntax initializer, AssignmentExpressionSyntax indent, string property)
    {
        var items = initializer.Expressions;
        var at = items.IndexOf(indent);
        var indentation = indent.GetLeadingTrivia().LastOrDefault() is var last && last.IsKind(SyntaxKind.WhitespaceTrivia)
            ? SyntaxFactory.TriviaList(last) : SyntaxFactory.TriviaList();

        var newLine = SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(property).WithLeadingTrivia(indentation),
                SyntaxFactory.Token(SyntaxKind.EqualsToken).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space),
                NewLineLiteral());

        var separators = items.GetSeparators().ToImmutableArray();
        var nodes = items.ToImmutableArray();

        // `indent,` is followed by a comma already: give the new item the same one.
        if (at < separators.Length)
            return initializer.WithExpressions(SyntaxFactory.SeparatedList(nodes.Insert(at + 1, newLine), separators.Insert(at, separators[at])));

        // `indent` is last with no comma: move its trailing trivia (a space or line break) after the new item.
        var lineBreak = indent.GetTrailingTrivia().Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia)).Take(1);
        var comma = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(
            separators.Length > 0 ? separators[separators.Length - 1].TrailingTrivia
            : lineBreak.Any() ? SyntaxFactory.TriviaList(lineBreak) : SyntaxFactory.TriviaList(SyntaxFactory.Space));
        return initializer.WithExpressions(SyntaxFactory.SeparatedList(
            nodes.SetItem(at, indent.WithoutTrailingTrivia()).Insert(at + 1, newLine.WithTrailingTrivia(indent.GetTrailingTrivia())),
            separators.Add(comma)));
    }
}
