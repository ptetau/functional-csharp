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
/// FP0011, indented JSON options only: adds <c>NewLine = "\n"</c> after <c>WriteIndented = true</c>,
/// so the output is the same on every OS. Other FP0011 findings get no fix: where a value should come from is a design choice.
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
        if (!diagnostic.Properties.ContainsKey(FunctionalAnalyzer.AddNewLineKey)) return;

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span)
                .AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault() is not { } indent
            || indent.Parent is not InitializerExpressionSyntax initializer) return;

        context.RegisterCodeFix(CodeAction.Create("Set NewLine = \"\\n\"",
            _ => Task.FromResult(FixHelpers.Replace(context.Document, root, initializer, WithNewLine(initializer, indent))),
            equivalenceKey: "FP0011.NewLine"), diagnostic);
    }

    // Inserts the assignment right after `indent`, laid out like its neighbours: same indentation, same separator.
    private static InitializerExpressionSyntax WithNewLine(InitializerExpressionSyntax initializer, AssignmentExpressionSyntax indent)
    {
        var items = initializer.Expressions;
        var at = items.IndexOf(indent);
        var indentation = indent.GetLeadingTrivia().LastOrDefault() is var last && last.IsKind(SyntaxKind.WhitespaceTrivia)
            ? SyntaxFactory.TriviaList(last) : SyntaxFactory.TriviaList();

        var newLine = SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("NewLine").WithLeadingTrivia(indentation),
                SyntaxFactory.Token(SyntaxKind.EqualsToken).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("\"\\n\"", "\n")));

        var separators = items.GetSeparators().ToList();
        var nodes = items.ToList();
        if (at < separators.Count)
        {
            // `indent,` is followed by a comma already: give the new item the same one.
            separators.Insert(at, separators[at]);
            nodes.Insert(at + 1, newLine);
        }
        else
        {
            // `indent` is last with no comma: move its trailing trivia (a space or line break) after the new item.
            var lineBreak = indent.GetTrailingTrivia().Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia)).Take(1);
            var comma = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(
                separators.Count > 0 ? separators[separators.Count - 1].TrailingTrivia
                : lineBreak.Any() ? SyntaxFactory.TriviaList(lineBreak) : SyntaxFactory.TriviaList(SyntaxFactory.Space));
            separators.Add(comma);
            nodes[at] = indent.WithoutTrailingTrivia();
            nodes.Insert(at + 1, newLine.WithTrailingTrivia(indent.GetTrailingTrivia()));
        }

        return initializer.WithExpressions(SyntaxFactory.SeparatedList(nodes, separators));
    }
}
