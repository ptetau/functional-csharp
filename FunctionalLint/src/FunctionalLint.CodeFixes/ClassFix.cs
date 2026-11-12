using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FunctionalLint.CodeFixes;

/// <summary>
/// FP0004: a class with only static members becomes a static class. Any other class becomes a record,
/// as long as that keeps its meaning. Classes with a primary constructor are left alone, because a
/// record would turn the constructor's parameters into public properties.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ClassFix)), Shared]
public sealed class ClassFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(FunctionalAnalyzer.NoObjects.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var found = await FixHelpers.TokenAt(context.Document, context.Span, context.CancellationToken).ConfigureAwait(false);
        if (found is not var (root, token) || token.Parent is not ClassDeclarationSyntax declaration) return;

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } type) return;

        // A fix to one part of a partial class would leave the other parts behind.
        if (type.DeclaringSyntaxReferences.Length != 1) return;

        var document = context.Document;
        var diagnostic = context.Diagnostics[0];

        if (CanBeStatic(type))
        {
            context.RegisterCodeFix(CodeAction.Create(
                $"Convert '{type.Name}' to a static class",
                _ => Task.FromResult(FixHelpers.Replace(document, root, declaration, ToStatic(declaration))),
                equivalenceKey: "FP0004"), diagnostic);
        }
        else if (CanBeRecord(type, declaration))
        {
            context.RegisterCodeFix(CodeAction.Create(
                $"Convert '{type.Name}' to a record",
                _ => Task.FromResult(FixHelpers.Replace(document, root, declaration, ToRecord(declaration))),
                equivalenceKey: "FP0004"), diagnostic);
        }
    }

    private static bool CanBeStatic(INamedTypeSymbol type) =>
        type.BaseType?.SpecialType == SpecialType.System_Object
        && type.Interfaces.IsEmpty
        && type.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared && m is not INamedTypeSymbol)
            .All(m => m.IsStatic);

    // Only plain data: a class with its own constructor or private state is a service, and turning it into a
    // record would change its equality without making it any less object-oriented.
    private static bool CanBeRecord(INamedTypeSymbol type, ClassDeclarationSyntax declaration) =>
        declaration.ParameterList is null
        && type.InstanceConstructors.All(c => c.IsImplicitlyDeclared)
        && !type.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsImplicitlyDeclared && f.DeclaredAccessibility != Accessibility.Public)
        && type.BaseType?.SpecialType == SpecialType.System_Object
        // Records generate Equals(object) and GetHashCode-based equality; a hand-written Equals(object) won't compile.
        && !type.GetMembers(nameof(Equals)).OfType<IMethodSymbol>()
            .Any(m => m.IsOverride && m.Parameters.Length == 1);

    private static ClassDeclarationSyntax ToStatic(ClassDeclarationSyntax declaration)
    {
        // static classes can't be sealed or abstract.
        var kept = SyntaxFactory.TokenList(declaration.Modifiers
            .Where(m => !m.IsKind(SyntaxKind.SealedKeyword) && !m.IsKind(SyntaxKind.AbstractKeyword)));
        var stripped = kept.Count == declaration.Modifiers.Count
            ? declaration
            : declaration.WithModifiers(kept);
        return FixHelpers.WithModifier(stripped, stripped.Modifiers, SyntaxKind.StaticKeyword, (c, m) => c.WithModifiers(m));
    }

    private static RecordDeclarationSyntax ToRecord(ClassDeclarationSyntax c) =>
        SyntaxFactory.RecordDeclaration(
            SyntaxKind.RecordDeclaration,
            c.AttributeLists,
            c.Modifiers,
            SyntaxFactory.Token(SyntaxKind.RecordKeyword).WithTriviaFrom(c.Keyword),
            default,
            c.Identifier,
            c.TypeParameterList,
            null,
            c.BaseList,
            c.ConstraintClauses,
            c.OpenBraceToken,
            c.Members,
            c.CloseBraceToken,
            c.SemicolonToken);
}
