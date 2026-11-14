using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FunctionalLint;

// FP0019: the functional subset of C#, as an allowlist. A core may use the kinds of syntax listed in Allowed below
// (and the ones other rules own, which report them with better advice). Everything else is reported, including kinds
// a future C# adds: they're outside the subset until someone reviews them and adds them here.
public sealed partial class FunctionalAnalyzer
{
    public static readonly DiagnosticDescriptor OutsideSubset = Rule("FP0019",
        "Only the functional subset of C#",
        "'{0}' is outside the subset: {1}. {2}.",
        "A core is written in a subset of C#: the kinds of syntax listed in Subset.cs. Everything else is reported, " +
        "including features a future C# adds, until someone reviews them. That rules out raw memory, locks, try/catch, " +
        "using, events, ref and out, async, checked, the ! operator, dynamic and object, virtual and abstract members, " +
        "and deriving from a record that isn't closed. A policy can allow more " +
        "(functional_lint.allowed_syntax).");

    public const string AllowedSyntaxKey = "functional_lint.allowed_syntax";

    // The language features a core may use. Reviewed against the sample apps, the Functional library and this
    // analyzer, which use 160-odd of Roslyn's ~290 kinds of syntax.
    private static readonly ImmutableHashSet<SyntaxKind> Allowed = ImmutableHashSet.Create(
        // Files, namespaces, usings, attributes
        SyntaxKind.CompilationUnit, SyntaxKind.FileScopedNamespaceDeclaration, SyntaxKind.NamespaceDeclaration,
        SyntaxKind.UsingDirective, SyntaxKind.ExternAliasDirective, SyntaxKind.AttributeList, SyntaxKind.Attribute,
        SyntaxKind.AttributeArgumentList, SyntaxKind.AttributeArgument, SyntaxKind.AttributeTargetSpecifier,
        SyntaxKind.NameEquals, SyntaxKind.NameColon, SyntaxKind.ExpressionColon,
        // Types: records, unions, structs, interfaces, enums, delegates, extensions (classes are FP0004's)
        SyntaxKind.RecordDeclaration, SyntaxKind.RecordStructDeclaration, SyntaxKind.StructDeclaration,
        SyntaxKind.InterfaceDeclaration, SyntaxKind.EnumDeclaration,
        SyntaxKind.EnumMemberDeclaration, SyntaxKind.DelegateDeclaration,
        SyntaxKind.BaseList, SyntaxKind.SimpleBaseType, SyntaxKind.PrimaryConstructorBaseType,
        SyntaxKind.TypeParameterList, SyntaxKind.TypeParameter, SyntaxKind.TypeParameterConstraintClause,
        SyntaxKind.TypeConstraint, SyntaxKind.ClassConstraint, SyntaxKind.StructConstraint, SyntaxKind.ConstructorConstraint,
        SyntaxKind.DefaultConstraint, SyntaxKind.AllowsConstraintClause, SyntaxKind.RefStructConstraint,
        // Members
        SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration, SyntaxKind.BaseConstructorInitializer,
        SyntaxKind.ThisConstructorInitializer, SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration,
        SyntaxKind.FieldDeclaration, SyntaxKind.OperatorDeclaration, SyntaxKind.ConversionOperatorDeclaration,
        SyntaxKind.AccessorList, SyntaxKind.GetAccessorDeclaration, SyntaxKind.InitAccessorDeclaration,
        SyntaxKind.ExplicitInterfaceSpecifier, SyntaxKind.ParameterList, SyntaxKind.BracketedParameterList,
        SyntaxKind.Parameter, SyntaxKind.ArrowExpressionClause, SyntaxKind.EqualsValueClause,
        // Type names
        SyntaxKind.IdentifierName, SyntaxKind.GenericName, SyntaxKind.QualifiedName, SyntaxKind.AliasQualifiedName,
        SyntaxKind.PredefinedType, SyntaxKind.NullableType, SyntaxKind.ArrayType, SyntaxKind.ArrayRankSpecifier,
        SyntaxKind.OmittedArraySizeExpression, SyntaxKind.TupleType, SyntaxKind.TupleElement, SyntaxKind.TypeArgumentList,
        SyntaxKind.OmittedTypeArgument,
        // Statements (loops and goto are FP0001's, throw is FP0007's)
        SyntaxKind.Block, SyntaxKind.LocalDeclarationStatement, SyntaxKind.VariableDeclaration, SyntaxKind.VariableDeclarator,
        SyntaxKind.LocalFunctionStatement, SyntaxKind.ExpressionStatement, SyntaxKind.ReturnStatement, SyntaxKind.IfStatement,
        SyntaxKind.ElseClause, SyntaxKind.SwitchStatement, SyntaxKind.SwitchSection, SyntaxKind.CaseSwitchLabel,
        SyntaxKind.CasePatternSwitchLabel, SyntaxKind.DefaultSwitchLabel, SyntaxKind.BreakStatement,
        SyntaxKind.YieldReturnStatement, SyntaxKind.YieldBreakStatement, SyntaxKind.EmptyStatement,
        // Expressions
        SyntaxKind.InvocationExpression, SyntaxKind.ArgumentList, SyntaxKind.BracketedArgumentList, SyntaxKind.Argument,
        SyntaxKind.SimpleMemberAccessExpression, SyntaxKind.ConditionalAccessExpression, SyntaxKind.MemberBindingExpression,
        SyntaxKind.ElementBindingExpression, SyntaxKind.ElementAccessExpression, SyntaxKind.ImplicitElementAccess,
        SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression, SyntaxKind.AnonymousObjectCreationExpression,
        SyntaxKind.AnonymousObjectMemberDeclarator, SyntaxKind.ArrayCreationExpression, SyntaxKind.ImplicitArrayCreationExpression,
        SyntaxKind.ObjectInitializerExpression, SyntaxKind.CollectionInitializerExpression, SyntaxKind.ComplexElementInitializerExpression,
        SyntaxKind.ArrayInitializerExpression, SyntaxKind.CollectionExpression, SyntaxKind.ExpressionElement, SyntaxKind.SpreadElement,
        SyntaxKind.WithExpression, SyntaxKind.WithInitializerExpression,
        SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression,
        SyntaxKind.ConditionalExpression, SyntaxKind.CoalesceExpression, SyntaxKind.ParenthesizedExpression,
        SyntaxKind.TupleExpression, SyntaxKind.DeclarationExpression, SyntaxKind.SingleVariableDesignation,
        SyntaxKind.ParenthesizedVariableDesignation, SyntaxKind.DiscardDesignation, SyntaxKind.CastExpression,
        SyntaxKind.AsExpression, SyntaxKind.IsExpression, SyntaxKind.TypeOfExpression, SyntaxKind.DefaultExpression,
        SyntaxKind.DefaultLiteralExpression, SyntaxKind.ThisExpression, SyntaxKind.BaseExpression,
        SyntaxKind.UncheckedExpression, SyntaxKind.UncheckedStatement, SyntaxKind.IndexExpression, SyntaxKind.RangeExpression,
        SyntaxKind.FieldExpression,
        SyntaxKind.InterpolatedStringExpression, SyntaxKind.InterpolatedStringText, SyntaxKind.Interpolation,
        SyntaxKind.InterpolationAlignmentClause, SyntaxKind.InterpolationFormatClause,
        SyntaxKind.StringLiteralExpression, SyntaxKind.Utf8StringLiteralExpression, SyntaxKind.CharacterLiteralExpression,
        SyntaxKind.NumericLiteralExpression, SyntaxKind.TrueLiteralExpression, SyntaxKind.FalseLiteralExpression,
        SyntaxKind.NullLiteralExpression,
        // Operators
        SyntaxKind.AddExpression, SyntaxKind.SubtractExpression, SyntaxKind.MultiplyExpression, SyntaxKind.DivideExpression,
        SyntaxKind.ModuloExpression, SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression,
        SyntaxKind.UnsignedRightShiftExpression, SyntaxKind.BitwiseAndExpression, SyntaxKind.BitwiseOrExpression,
        SyntaxKind.ExclusiveOrExpression, SyntaxKind.BitwiseNotExpression, SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalOrExpression, SyntaxKind.LogicalNotExpression, SyntaxKind.UnaryMinusExpression,
        SyntaxKind.UnaryPlusExpression, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression,
        SyntaxKind.LessThanExpression, SyntaxKind.LessThanOrEqualExpression, SyntaxKind.GreaterThanExpression,
        SyntaxKind.GreaterThanOrEqualExpression,
        // Patterns and switch expressions
        SyntaxKind.IsPatternExpression, SyntaxKind.SwitchExpression, SyntaxKind.SwitchExpressionArm, SyntaxKind.WhenClause,
        SyntaxKind.ConstantPattern, SyntaxKind.DeclarationPattern, SyntaxKind.VarPattern, SyntaxKind.DiscardPattern,
        SyntaxKind.RecursivePattern, SyntaxKind.PositionalPatternClause, SyntaxKind.PropertyPatternClause, SyntaxKind.Subpattern,
        SyntaxKind.TypePattern, SyntaxKind.RelationalPattern, SyntaxKind.AndPattern, SyntaxKind.OrPattern, SyntaxKind.NotPattern,
        SyntaxKind.ParenthesizedPattern, SyntaxKind.ListPattern, SyntaxKind.SlicePattern,
        // LINQ query syntax
        SyntaxKind.QueryExpression, SyntaxKind.QueryBody, SyntaxKind.FromClause, SyntaxKind.LetClause, SyntaxKind.WhereClause,
        SyntaxKind.JoinClause, SyntaxKind.JoinIntoClause, SyntaxKind.OrderByClause, SyntaxKind.AscendingOrdering,
        SyntaxKind.DescendingOrdering, SyntaxKind.SelectClause, SyntaxKind.GroupClause, SyntaxKind.QueryContinuation,
        // Reported by other rules, with better advice: loops and goto (FP0001), assignment (FP0002), setters (FP0003),
        // classes (FP0004), throw (FP0007)
        SyntaxKind.ForStatement, SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement, SyntaxKind.WhileStatement,
        SyntaxKind.DoStatement, SyntaxKind.ContinueStatement, SyntaxKind.GotoStatement, SyntaxKind.GotoCaseStatement,
        SyntaxKind.GotoDefaultStatement, SyntaxKind.LabeledStatement,
        SyntaxKind.SimpleAssignmentExpression, SyntaxKind.AddAssignmentExpression, SyntaxKind.SubtractAssignmentExpression,
        SyntaxKind.MultiplyAssignmentExpression, SyntaxKind.DivideAssignmentExpression, SyntaxKind.ModuloAssignmentExpression,
        SyntaxKind.AndAssignmentExpression, SyntaxKind.OrAssignmentExpression, SyntaxKind.ExclusiveOrAssignmentExpression,
        SyntaxKind.LeftShiftAssignmentExpression, SyntaxKind.RightShiftAssignmentExpression,
        SyntaxKind.UnsignedRightShiftAssignmentExpression, SyntaxKind.CoalesceAssignmentExpression,
        SyntaxKind.PreIncrementExpression, SyntaxKind.PreDecrementExpression, SyntaxKind.PostIncrementExpression,
        SyntaxKind.PostDecrementExpression, SyntaxKind.SetAccessorDeclaration, SyntaxKind.ClassDeclaration,
        SyntaxKind.DestructorDeclaration, SyntaxKind.ThrowStatement, SyntaxKind.ThrowExpression,
        // Code that doesn't parse: the compiler reports it
        SyntaxKind.IncompleteMember, SyntaxKind.UnknownAccessorDeclaration);

    // Kinds newer than the Roslyn this analyzer is built against (4.12), allowed or excluded by name. At run time the
    // compiler's own Roslyn names them. C# 15 unions, C# 14 extension blocks, collection expression arguments.
    private static readonly ImmutableHashSet<string> AllowedByName = ImmutableHashSet.Create(StringComparer.Ordinal,
        "UnionDeclaration", "ExtensionBlockDeclaration", "WithElement", "ExtensionMemberCref");

    private static readonly ImmutableDictionary<string, (string Feature, string Why, string Instead)> ExcludedByName =
        ImmutableDictionary.Create<string, (string, string, string)>(StringComparer.Ordinal)
            .SetItem("UnsafeExpression", ("unsafe", "it works on raw memory", "Use arrays or immutable collections"));

    private static bool IsAllowed(SyntaxKind kind) => Allowed.Contains(kind) || AllowedByName.Contains(kind.ToString());

    private static (string Feature, string Why, string Instead)? Exclusion(SyntaxKind kind) =>
        ExcludedByKind.TryGetValue(kind, out var info) ? info
        : ExcludedByName.TryGetValue(kind.ToString(), out var named) ? named
        : null;

    // Why each feature is left out, and what to do instead. Kinds not listed here and not allowed get a general message.
    private static readonly ImmutableArray<(string Feature, string Why, string Instead, ImmutableArray<SyntaxKind> Kinds)> Excluded =
    [
        ("lock", "a core has no shared state to guard", "Move it to a shell project",
            [SyntaxKind.LockStatement]),
        ("try", "exceptions aren't control flow in a core", "Return a Result instead",
            [SyntaxKind.TryStatement, SyntaxKind.CatchClause, SyntaxKind.CatchDeclaration, SyntaxKind.CatchFilterClause, SyntaxKind.FinallyClause]),
        ("using", "disposing is a side effect", "Move it to a shell project",
            [SyntaxKind.UsingStatement]),
        ("event", "an event is a mutable list of handlers", "Return the new state instead",
            [SyntaxKind.EventDeclaration, SyntaxKind.EventFieldDeclaration, SyntaxKind.AddAccessorDeclaration, SyntaxKind.RemoveAccessorDeclaration]),
        ("unsafe", "it works on raw memory", "Use arrays or immutable collections",
            [SyntaxKind.UnsafeStatement, SyntaxKind.FixedStatement, SyntaxKind.PointerType,
             SyntaxKind.AddressOfExpression, SyntaxKind.PointerIndirectionExpression, SyntaxKind.PointerMemberAccessExpression,
             SyntaxKind.FunctionPointerType, SyntaxKind.FunctionPointerParameterList, SyntaxKind.FunctionPointerParameter,
             SyntaxKind.FunctionPointerCallingConvention, SyntaxKind.FunctionPointerUnmanagedCallingConventionList,
             SyntaxKind.FunctionPointerUnmanagedCallingConvention, SyntaxKind.SizeOfExpression]),
        ("stackalloc", "it makes a buffer to fill in place", "Use an array or an immutable collection",
            [SyntaxKind.StackAllocArrayCreationExpression, SyntaxKind.ImplicitStackAllocArrayCreationExpression]),
        ("ref", "it lets code change a value in place", "Return a new value instead",
            [SyntaxKind.RefExpression, SyntaxKind.RefType, SyntaxKind.ScopedType, SyntaxKind.RefTypeExpression,
             SyntaxKind.RefValueExpression, SyntaxKind.MakeRefExpression, SyntaxKind.ArgListExpression]),
        ("async", "async is for I/O, which is the shell's job", "Take the result as a parameter",
            [SyntaxKind.AwaitExpression]),
        ("checked", "it throws on overflow", "Use a wider type, or check the range first",
            [SyntaxKind.CheckedExpression, SyntaxKind.CheckedStatement]),
        ("!", "it hides a possible null", "Handle the null: a pattern, ??, or an Option",
            [SyntaxKind.SuppressNullableWarningExpression]),
        ("top-level statements", "a program's entry point is the shell", "Move it to a shell project",
            [SyntaxKind.GlobalStatement]),
    ];

    // Modifiers that bring in the same features on a declaration.
    private static readonly ImmutableArray<(SyntaxKind Modifier, string Feature, string Why, string Instead)> ExcludedModifiers =
    [
        (SyntaxKind.UnsafeKeyword, "unsafe", "it works on raw memory", "Use arrays or immutable collections"),
        (SyntaxKind.ExternKeyword, "extern", "it calls native code", "Call native code from a shell project"),
        (SyntaxKind.VolatileKeyword, "volatile", "it marks state shared between threads", "Move it to a shell project"),
        (SyntaxKind.FixedKeyword, "fixed", "it declares a raw memory buffer", "Use an array or an immutable collection"),
        (SyntaxKind.AsyncKeyword, "async", "async is for I/O, which is the shell's job", "Take the result as a parameter"),
    ];

    private static readonly ImmutableDictionary<SyntaxKind, (string Feature, string Why, string Instead)> ExcludedByKind =
        Excluded.SelectMany(e => e.Kinds.Select(kind => (Kind: kind, Info: (e.Feature, e.Why, e.Instead))))
            .GroupBy(e => e.Kind)
            .ToImmutableDictionary(g => g.Key, g => g.First().Info);

    private const string UnknownWhy = "it isn't on the subset's list";
    private const string UnknownInstead = "Rewrite it, or ask the project owner to allow it";

    // Everything in one file the subset leaves out. `skip` says whether a node is in an adapter or legacy code.
    private static IEnumerable<Diagnostic> OutsideTheSubset(SemanticModel model, ImmutableArray<string> allowedExtra,
        Func<SyntaxNode, bool> skip, System.Threading.CancellationToken ct)
    {
        bool Allows(string feature) => allowedExtra.Contains(feature, StringComparer.OrdinalIgnoreCase);
        var root = model.SyntaxTree.GetRoot(ct);

        var kinds = root.DescendantNodes()
            .Where(node => !IsAllowed(node.Kind()))
            .Select(node => (Node: node, Info: Exclusion(node.Kind()) ?? (Feature: node.Kind().ToString(), Why: UnknownWhy, Instead: UnknownInstead)))
            // Report a feature once where it starts: a try statement, not also its catch and finally.
            .Where(f => !f.Node.Ancestors().Any(a => Exclusion(a.Kind()) is { } outer && outer.Feature == f.Info.Feature))
            .Where(f => !Allows(f.Info.Feature) && !Allows(f.Node.Kind().ToString()) && !IsRequiredByContract(model, f.Node, f.Info.Feature, ct))
            .Select(f => (f.Node, f.Info.Feature, f.Info.Why, f.Info.Instead, At: Anchor(f.Node)));

        var modifiers = root.DescendantNodes().OfType<MemberDeclarationSyntax>()
            .Select(member => (Node: (SyntaxNode)member, Modifiers: member.Modifiers))
            .Concat(root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Select(local => (Node: (SyntaxNode)local, local.Modifiers)))
            .Concat(root.DescendantNodes().OfType<LambdaExpressionSyntax>().Select(lambda => (Node: (SyntaxNode)lambda, lambda.Modifiers)))
            .SelectMany(m => m.Modifiers.Select(token => (m.Node, Token: token)))
            .SelectMany(m => ExcludedModifiers.Where(x => m.Token.IsKind(x.Modifier))
                .Select(x => (m.Node, x.Feature, x.Why, x.Instead, At: m.Token.GetLocation())))
            .Where(m => !Allows(m.Feature) && !IsRequiredByContract(model, m.Node, m.Feature, ct));

        // `using var reader = ...;` is a local declaration with a using keyword, not a using statement.
        var usingDeclarations = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
            .Where(local => local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
            .Select(local => (Node: (SyntaxNode)local, Feature: "using", Why: "disposing is a side effect",
                Instead: "Move it to a shell project", At: local.UsingKeyword.GetLocation()))
            .Where(u => !Allows(u.Feature));

        // `out` and `ref` parameters return by writing to the caller's variable. Overrides and interface
        // implementations are left alone: their signature is someone else's.
        var parameters = root.DescendantNodes().OfType<ParameterSyntax>()
            .SelectMany(p => p.Modifiers.Where(t => t.IsKind(SyntaxKind.OutKeyword) || t.IsKind(SyntaxKind.RefKeyword))
                .Select(t => (Node: (SyntaxNode)p, Feature: t.Text, Why: "it writes to the caller's variable",
                    Instead: "Return a tuple, an Option or a Result", At: t.GetLocation())))
            .Where(p => !Allows(p.Feature) && !IsRequiredByContract(model, p.Node, p.Feature, ct));

        // `virtual` and `abstract` members send a call to code a subtype picks, out of sight. An abstract type is open:
        // anyone can add a subtype. A closed record says the same thing and the compiler checks every switch over it.
        // Interfaces are contracts, so their static abstract members (generic math) are left alone.
        var dispatch = root.DescendantNodes().OfType<MemberDeclarationSyntax>()
            .Where(member => member.Parent is not InterfaceDeclarationSyntax)
            .SelectMany(member => member.Modifiers
                .Where(t => t.IsKind(SyntaxKind.VirtualKeyword) || t.IsKind(SyntaxKind.AbstractKeyword))
                .Select(t => member is BaseTypeDeclarationSyntax type
                    ? type.Modifiers.Any(m => m.Text == "closed")
                        ? default
                        : (Node: (SyntaxNode)member, Feature: t.Text, Why: "anyone can add a subtype, so a switch over it can't be checked",
                            Instead: "Mark it 'closed' instead", At: t.GetLocation())
                    : (Node: (SyntaxNode)member, Feature: t.Text, Why: "a subtype picks what runs, unseen",
                        Instead: "Use a switch on closed cases, or a function", At: t.GetLocation())))
            .Where(d => d.Node is not null && !Allows(d.Feature));

        // A record may derive only from a closed record: then every switch over the base is checked for each case.
        var inheritance = root.DescendantNodes().OfType<RecordDeclarationSyntax>()
            .Where(record => record.BaseList is not null)
            .Select(record => (Record: record, Base: model.GetDeclaredSymbol(record, ct)?.BaseType))
            .Where(r => r.Base is { IsRecord: true } b && !IsClosed(b))
            .Select(r => (Node: (SyntaxNode)r.Record, Feature: "inheritance",
                Why: $"'{r.Base?.Name}' isn't closed, so a switch over it can't be checked",
                Instead: $"Mark '{r.Base?.Name}' closed", At: r.Record.BaseList?.Types.FirstOrDefault()?.Type.GetLocation() ?? r.Record.Identifier.GetLocation()))
            .Where(i => !Allows(i.Feature));

        // `object` hides the type, so using the value means a cast or a type test. Overrides and interface
        // implementations keep it in their signature (Equals(object? obj)): that's someone else's.
        var objects = root.DescendantNodes().OfType<PredefinedTypeSyntax>()
            .Where(type => type.Keyword.IsKind(SyntaxKind.ObjectKeyword))
            .Select(type => (Node: (SyntaxNode)type, Feature: "object", Why: "it hides the type",
                Instead: "Use the real type, a type parameter, or a closed record", At: type.GetLocation()))
            .Where(o => !Allows(o.Feature) && !IsRequiredByContract(model, o.Node, o.Feature, ct));

        // `dynamic` turns off type checking. It's a type name, so only the semantic model can tell.
        var dynamics = root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText == "dynamic"
                && model.GetTypeInfo(name, ct).Type is { TypeKind: TypeKind.Dynamic })
            .Select(name => (Node: (SyntaxNode)name, Feature: "dynamic", Why: "it turns off type checking",
                Instead: "Use a real type, or a closed record for the cases", At: name.GetLocation()))
            .Where(d => !Allows(d.Feature));

        return kinds.Concat(modifiers).Concat(usingDeclarations).Concat(parameters).Concat(dynamics)
            .Concat(dispatch).Concat(inheritance).Concat(objects)
            .Where(f => !skip(f.Node))
            .Select(f => Diagnostic.Create(OutsideSubset, f.At, f.Feature, f.Why, f.Instead));
    }

    // Report on the keyword, so a whole block isn't underlined.
    private static Location Anchor(SyntaxNode node) => node switch
    {
        LockStatementSyntax s => s.LockKeyword.GetLocation(),
        TryStatementSyntax s => s.TryKeyword.GetLocation(),
        UsingStatementSyntax s => s.UsingKeyword.GetLocation(),
        UnsafeStatementSyntax s => s.UnsafeKeyword.GetLocation(),
        FixedStatementSyntax s => s.FixedKeyword.GetLocation(),
        CheckedStatementSyntax s => s.Keyword.GetLocation(),
        EventFieldDeclarationSyntax s => s.EventKeyword.GetLocation(),
        EventDeclarationSyntax s => s.EventKeyword.GetLocation(),
        GlobalStatementSyntax s => s.GetFirstToken().GetLocation(),
        _ => node.GetLocation(),
    };

    // An override or interface implementation can't choose to be async or to have out parameters: the library it
    // plugs into decides (a Roslyn code fix's RegisterCodeFixesAsync, IReadOnlyDictionary.TryGetValue).
    private static bool IsRequiredByContract(SemanticModel model, SyntaxNode node, string feature, System.Threading.CancellationToken ct)
    {
        if (feature is not ("async" or "out" or "ref" or "object")) return false;
        // Only a signature is someone else's: `object` in the body is the member's own choice.
        if (feature == "object" && node.Ancestors().Any(a => a is BlockSyntax or ArrowExpressionClauseSyntax)) return false;
        var member = node.AncestorsAndSelf().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault() as SyntaxNode
            ?? node.AncestorsAndSelf().OfType<BasePropertyDeclarationSyntax>().FirstOrDefault();
        if (member is null || model.GetDeclaredSymbol(member, ct) is not { } symbol) return false;
        // A lambda or local function inside the member chose its own shape.
        if (node.AncestorsAndSelf().TakeWhile(a => a != member).Any(a => a is LambdaExpressionSyntax or LocalFunctionStatementSyntax))
            return false;
        return symbol.IsOverride
            || symbol.ContainingType.AllInterfaces.SelectMany(i => i.GetMembers())
                .Any(m => SymbolEqualityComparer.Default.Equals(symbol.ContainingType.FindImplementationForInterfaceMember(m), symbol));
    }
}
