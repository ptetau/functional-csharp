using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace FunctionalLint.CodeFixes;

/// <summary>
/// FP0010: changes an async void method or local function to return Task.
/// Overrides and interface implementations are left alone, since their signature is fixed elsewhere.
/// Lambdas are left alone too: their type comes from the delegate they're assigned to.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncVoidFix)), Shared]
public sealed class AsyncVoidFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(FunctionalAnalyzer.AsyncVoid.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var found = await FixHelpers.TokenAt(context.Document, context.Span, context.CancellationToken).ConfigureAwait(false);
        if (found is not var (root, token)) return;

        var (declaration, returnType) = token.Parent switch
        {
            MethodDeclarationSyntax m => ((SyntaxNode?)m, m.ReturnType),
            LocalFunctionStatementSyntax l => (l, l.ReturnType),
            _ => (null, null),
        };
        if (declaration is null || returnType is null) return;

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null || model.GetDeclaredSymbol(declaration, context.CancellationToken) is not IMethodSymbol method) return;
        if (method.IsOverride || !method.ExplicitInterfaceImplementations.IsEmpty || ImplementsInterface(method)) return;

        context.RegisterCodeFix(CodeAction.Create("Return Task", _ =>
        {
            var task = SyntaxFactory.IdentifierName("Task").WithTriviaFrom(returnType);
            var newRoot = root.ReplaceNode(returnType, task);

            var taskInScope = model.LookupNamespacesAndTypes(returnType.SpanStart, name: "Task")
                .Any(s => s.ToDisplayString() == "System.Threading.Tasks.Task");
            if (!taskInScope && newRoot is CompilationUnitSyntax unit)
                newRoot = unit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Threading.Tasks"))
                    .WithAdditionalAnnotations(Formatter.Annotation));

            return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
        }, equivalenceKey: "FP0010"), context.Diagnostics[0]);
    }

    private static bool ImplementsInterface(IMethodSymbol method) =>
        method.ContainingType.AllInterfaces
            .SelectMany(i => i.GetMembers().OfType<IMethodSymbol>())
            .Any(m => SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(m), method));
}
