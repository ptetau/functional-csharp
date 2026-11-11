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
/// FP0003: makes fields readonly, turns 'set' into 'init' (or drops it on static properties),
/// and makes record structs readonly.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MutableStateFix)), Shared]
public sealed class MutableStateFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(FunctionalAnalyzer.NoMutableState.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var found = await FixHelpers.TokenAt(context.Document, context.Span, context.CancellationToken).ConfigureAwait(false);
        if (found is not var (root, token)) return;
        var diagnostic = context.Diagnostics[0];
        var document = context.Document;

        switch (token.Parent)
        {
            case VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax field } declarator
                when await CanBeSetOnceAsync(document, declarator, context.CancellationToken).ConfigureAwait(false):
                Register(context, diagnostic, "Make field readonly", "FP0003",
                    () => FixHelpers.Replace(document, root, field, MakeReadonly(field)));
                break;

            case BasePropertyDeclarationSyntax property
                when property.AccessorList?.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) is { } setter:
                if (property.Modifiers.Any(SyntaxKind.StaticKeyword))
                {
                    // Static properties can't have 'init'. Only an auto-property's setter can be dropped safely.
                    if (setter.Body is null && setter.ExpressionBody is null)
                        Register(context, diagnostic, "Remove the setter", "FP0003",
                            () => FixHelpers.Replace(document, root, property,
                                property.WithAccessorList(property.AccessorList!.RemoveNode(setter, SyntaxRemoveOptions.KeepNoTrivia))));
                }
                else
                {
                    Register(context, diagnostic, "Change 'set' to 'init'", "FP0003",
                        () => FixHelpers.Replace(document, root, setter, ToInit(setter)));
                }
                break;

            // `record struct P(int X)`: the struct itself, or one of its positional properties.
            case var node when node?.FirstAncestorOrSelf<RecordDeclarationSyntax>() is { } record
                               && record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
                               && !record.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
                               && (node == record || node.Parent?.Parent == record.ParameterList?.Parent):
                Register(context, diagnostic, "Make record struct readonly", "FP0003",
                    () => FixHelpers.Replace(document, root, record,
                        FixHelpers.WithModifier(record, record.Modifiers, SyntaxKind.ReadOnlyKeyword, (r, m) => r.WithModifiers(m))));
                break;
        }
    }

    // A readonly field can only be set by its initializer, a constructor or an init accessor (a `set` accessor
    // becomes one under this same fix). Without any of those it would stay default forever, which compiles but
    // leaves a dead field (e.g. a struct filled in by object initialisers).
    private static async Task<bool> CanBeSetOnceAsync(Document document, VariableDeclaratorSyntax declarator, CancellationToken ct)
    {
        if (declarator.Initializer is not null) return true;
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(declarator, ct) is not IFieldSymbol field) return false;
        var type = field.ContainingType;
        return field.IsStatic
            ? type.StaticConstructors.Any()
            : type.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared)
              || type.GetMembers().OfType<IPropertySymbol>().Any(p => !p.IsStatic && p.SetMethod is not null)
              || type.DeclaringSyntaxReferences.Any(r => r.GetSyntax(ct) is TypeDeclarationSyntax { ParameterList: not null });
    }

    private static FieldDeclarationSyntax MakeReadonly(FieldDeclarationSyntax field)
    {
        // 'volatile' and 'readonly' can't be combined, and volatile means nothing on a readonly field.
        var volatileToken = field.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.VolatileKeyword));
        if (volatileToken != default)
            return field.WithModifiers(field.Modifiers.Replace(volatileToken,
                SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword).WithTriviaFrom(volatileToken)));

        return FixHelpers.WithModifier(field, field.Modifiers, SyntaxKind.ReadOnlyKeyword, (f, m) => f.WithModifiers(m));
    }

    private static AccessorDeclarationSyntax ToInit(AccessorDeclarationSyntax setter) =>
        SyntaxFactory.AccessorDeclaration(
            SyntaxKind.InitAccessorDeclaration,
            setter.AttributeLists,
            setter.Modifiers,
            SyntaxFactory.Token(SyntaxKind.InitKeyword).WithTriviaFrom(setter.Keyword),
            setter.Body,
            setter.ExpressionBody,
            setter.SemicolonToken);

    private static void Register(CodeFixContext context, Diagnostic diagnostic, string title, string key, System.Func<Document> fix) =>
        context.RegisterCodeFix(
            CodeAction.Create(title, (CancellationToken _) => Task.FromResult(fix()), equivalenceKey: key),
            diagnostic);
}
