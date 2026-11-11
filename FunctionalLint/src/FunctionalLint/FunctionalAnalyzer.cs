using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FunctionalLint;

/// <summary>
/// Enforces a functional subset of C#:
///   FP0001  no loops
///   FP0002  no reassignment or mutation
///   FP0003  no mutable fields, setters or mutable record structs
///   FP0004  no classes (use records for data, static classes for functions)
///   FP0005  no creating mutable collections
///   FP0006  legacy code may only be called from an [ImperativeAdapter]
///   FP0007  no throwing
///   FP0008  no mutable collection types in signatures
///   FP0009  adapters must not expose legacy types
///   FP0010  no async void
///   FP0011  no side-effecting APIs (clock, random, I/O, console...)
///
/// Code inside an [ImperativeAdapter] type or member, or inside a namespace listed in
/// functional_lint.legacy_namespaces, is exempt from every rule except FP0009.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FunctionalAnalyzer : DiagnosticAnalyzer
{
    // Rules -------------------------------------------------------------------

    // Messages are what a developer or agent sees in build output, so each one names the
    // offending code and says what to write instead. Descriptions add the "why" and show
    // up in the IDE and in SARIF. Help links point at docs/rules/<id>.md.

    public static readonly DiagnosticDescriptor NoLoops = Rule("FP0001",
        "Loops are not allowed",
        "'{0}' loops are not allowed. Use LINQ, Aggregate or recursion instead.",
        "Loops work by changing a variable on each pass. Express the result instead: Select to transform, " +
        "Where to filter, Sum/Count/Any to summarise, Aggregate to fold, recursion for trees.");

    public static readonly DiagnosticDescriptor NoMutation = Rule("FP0002",
        "Mutation is not allowed",
        "'{0}' is changed here. Bind a new name or use a 'with' expression instead.",
        "Values never change once created. Instead of 'x = x + 1' write 'var next = x + 1'. " +
        "Instead of setting a property write 'item with { Name = name }'.");

    public static readonly DiagnosticDescriptor NoMutableState = Rule("FP0003",
        "Mutable state is not allowed",
        "{0} '{1}' is mutable. {2}",
        "Fields must be readonly, properties get-only or init, and record structs readonly. " +
        "A code fix applies the change.");

    public static readonly DiagnosticDescriptor NoObjects = Rule("FP0004",
        "Classes are not allowed",
        "Class '{0}' is not allowed. Use a record for data or a static class for functions.",
        "Plain classes mix data with behaviour and usually hold mutable state. Put data in records and " +
        "functions in static classes. A code fix converts the class when it is safe to.");

    public static readonly DiagnosticDescriptor NoMutableTypes = Rule("FP0005",
        "Mutable collection types are not allowed",
        "'{0}' is mutable. Use {1} instead.",
        "Build collections with System.Collections.Immutable, LINQ or collection expressions " +
        "targeting immutable types, such as 'ImmutableArray<int> xs = [1, 2, 3]'.");

    public static readonly DiagnosticDescriptor NoDirectImperative = Rule("FP0006",
        "Legacy code must be called through an adapter",
        "'{0}' is legacy imperative code. Call it through an [ImperativeAdapter] instead.",
        "Functional code reaches the namespaces in functional_lint.legacy_namespaces only through a type or " +
        "member marked [ImperativeAdapter], so every crossing point is explicit and searchable.");

    public static readonly DiagnosticDescriptor NoThrow = Rule("FP0007",
        "Throwing is not allowed",
        "Don't throw here. Return an Error on the Result railway, or throw from an [ImperativeAdapter].",
        "Exceptions are hidden control flow. Return a Result (or similar union) so the caller must handle failure. " +
        "Adapters may throw and catch at the edge of the system.");

    public static readonly DiagnosticDescriptor MutableSignature = Rule("FP0008",
        "Mutable collection types are not allowed in signatures",
        "'{0}' lets callers change the collection. Use {1} instead.",
        "Signatures expose read-only or immutable collections so neither side can change what the other holds. " +
        "A code fix swaps IList, ICollection, IDictionary, ISet, List, Dictionary and HashSet for their read-only interfaces.");

    public static readonly DiagnosticDescriptor AdapterLeak = Rule("FP0009",
        "Adapters must not expose legacy types",
        "'{0}' exposes legacy type '{1}'. Map it to a functional type (a record or Result) instead.",
        "An adapter is the only bridge to legacy code. If it hands out legacy types the bridge leaks. " +
        "Take legacy services in the constructor, return records, immutable collections or Result.");

    public static readonly DiagnosticDescriptor AsyncVoid = Rule("FP0010",
        "async void is not allowed",
        "'{0}' is async void, so its errors can't be observed. Return Task instead.",
        "An async void method can't be awaited and its exceptions crash the process. Return Task or Task<T>. " +
        "A code fix changes the return type for methods and local functions.");

    public static readonly DiagnosticDescriptor ImpureApi = Rule("FP0011",
        "Side-effecting API",
        "'{0}' has side effects. Call it from an [ImperativeAdapter], or pass the value in.",
        "Functions that read the clock, random numbers, the console, files or the network give different answers " +
        "for the same input. Pass the value in as a parameter (e.g. 'DateTime now'), or wrap the API in an adapter.");

    public const string HelpLinkBase = "https://github.com/ptetau/functional-csharp/blob/main/FunctionalLint/docs/rules/";

    private static DiagnosticDescriptor Rule(string id, string title, string message, string description) =>
        new(id, title, message, "Functional", DiagnosticSeverity.Error, isEnabledByDefault: true,
            description: description, helpLinkUri: HelpLinkBase + id + ".md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        NoLoops, NoMutation, NoMutableState, NoObjects, NoMutableTypes, NoDirectImperative,
        NoThrow, MutableSignature, AdapterLeak, AsyncVoid, ImpureApi);

    // Configuration -----------------------------------------------------------

    // Matched by name, so consumers define the attribute themselves (see README).
    private const string AdapterAttributeName = "ImperativeAdapterAttribute";

    // .editorconfig / .globalconfig keys. Both take comma-separated lists.
    private const string LegacyNamespacesKey = "functional_lint.legacy_namespaces";
    private const string ImpureApisKey = "functional_lint.impure_apis";

    private static readonly (string MetadataName, string Alternative)[] MutableTypes =
    {
        ("System.Collections.Generic.List`1", "ImmutableArray<T> or ImmutableList<T>"),
        ("System.Collections.Generic.Dictionary`2", "ImmutableDictionary<TKey, TValue>"),
        ("System.Collections.Generic.HashSet`1", "ImmutableHashSet<T>"),
        ("System.Collections.Generic.SortedDictionary`2", "ImmutableSortedDictionary<TKey, TValue>"),
        ("System.Collections.Generic.SortedSet`1", "ImmutableSortedSet<T>"),
        ("System.Collections.Generic.Queue`1", "ImmutableQueue<T>"),
        ("System.Collections.Generic.Stack`1", "ImmutableStack<T>"),
        ("System.Collections.Generic.LinkedList`1", "ImmutableList<T>"),
        ("System.Collections.Concurrent.ConcurrentDictionary`2", "ImmutableDictionary<TKey, TValue>"),
        ("System.Text.StringBuilder", "string interpolation or string.Join"),
    };

    private static readonly (string MetadataName, string Alternative)[] MutableInterfaces =
    {
        ("System.Collections.Generic.IList`1", "IReadOnlyList<T> or ImmutableArray<T>"),
        ("System.Collections.Generic.ICollection`1", "IReadOnlyCollection<T>"),
        ("System.Collections.Generic.IDictionary`2", "IReadOnlyDictionary<TKey, TValue>"),
        ("System.Collections.Generic.ISet`1", "IReadOnlySet<T>"),
        ("System.Collections.IList", "IReadOnlyList<T>"),
        ("System.Collections.IDictionary", "IReadOnlyDictionary<TKey, TValue>"),
    };

    // A whole type ("System.Console") or one member ("System.DateTime.Now").
    private static readonly string[] DefaultImpureApis =
    {
        "System.DateTime.Now", "System.DateTime.UtcNow", "System.DateTime.Today",
        "System.DateTimeOffset.Now", "System.DateTimeOffset.UtcNow",
        "System.TimeProvider.System",
        "System.Guid.NewGuid",
        "System.Random",
        "System.Security.Cryptography.RandomNumberGenerator",
        "System.Console",
        "System.Environment",
        "System.IO.File", "System.IO.Directory", "System.IO.FileInfo", "System.IO.DirectoryInfo",
        "System.IO.FileStream", "System.IO.StreamReader", "System.IO.StreamWriter",
        "System.Net.Http.HttpClient",
        "System.Diagnostics.Process", "System.Diagnostics.Stopwatch",
        "System.Threading.Thread.Sleep", "System.Threading.Tasks.Task.Delay",
    };

    private static readonly OperationKind[] ReferenceKinds =
    {
        OperationKind.Invocation,
        OperationKind.ObjectCreation,
        OperationKind.FieldReference,
        OperationKind.PropertyReference,
        OperationKind.MethodReference,
        OperationKind.EventReference,
    };

    // Registration ------------------------------------------------------------

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // GuardOp / GuardSymbol skip adapters and legacy namespaces.
        context.RegisterOperationAction(GuardOp(AnalyzeLoop), OperationKind.Loop);
        context.RegisterOperationAction(GuardOp(AnalyzeAssignment),
            OperationKind.SimpleAssignment,
            OperationKind.CompoundAssignment,
            OperationKind.CoalesceAssignment,
            OperationKind.DeconstructionAssignment);
        context.RegisterOperationAction(GuardOp(AnalyzeIncrement), OperationKind.Increment, OperationKind.Decrement);
        context.RegisterOperationAction(GuardOp(AnalyzeEventAssignment), OperationKind.EventAssignment);
        context.RegisterOperationAction(GuardOp(AnalyzeBoundary), ReferenceKinds);
        context.RegisterOperationAction(GuardOp(AnalyzeThrow), OperationKind.Throw);
        context.RegisterOperationAction(GuardOp(AnalyzeAsyncVoidFunction),
            OperationKind.AnonymousFunction, OperationKind.LocalFunction);

        context.RegisterSymbolAction(GuardSymbol(AnalyzeField), SymbolKind.Field);
        context.RegisterSymbolAction(GuardSymbol(AnalyzeProperty), SymbolKind.Property);
        context.RegisterSymbolAction(GuardSymbol(AnalyzeType), SymbolKind.NamedType);
        context.RegisterSymbolAction(GuardSymbol(AnalyzeAsyncVoidMethod), SymbolKind.Method);

        // Runs only on adapters, so it is deliberately not guarded.
        context.RegisterSymbolAction(AnalyzeAdapterSignature, SymbolKind.Method, SymbolKind.Property, SymbolKind.Field);

        context.RegisterCompilationStartAction(start =>
        {
            var compilation = start.Compilation;
            var mutableTypes = Resolve(compilation, MutableTypes);
            var inSignatures = Resolve(compilation, MutableTypes.Concat(MutableInterfaces));
            var impureByConfig = new ConcurrentDictionary<string, ImpureApis>(StringComparer.Ordinal);

            start.RegisterOperationAction(
                GuardOp(ctx => AnalyzeCreation(ctx, mutableTypes)),
                OperationKind.ObjectCreation, OperationKind.CollectionExpression, OperationKind.Invocation);

            start.RegisterSymbolAction(
                GuardSymbol(ctx => AnalyzeSignature(ctx, inSignatures)),
                SymbolKind.Method, SymbolKind.Property, SymbolKind.Field);

            start.RegisterOperationAction(GuardOp(ctx =>
            {
                var extra = OptionValue(ctx.Options, ctx.Operation.Syntax.SyntaxTree, ImpureApisKey);
                var apis = impureByConfig.GetOrAdd(extra, e => ImpureApis.Create(compilation, e));
                AnalyzeImpure(ctx, apis);
            }), ReferenceKinds);
        });
    }

    // FP0001 Loops ------------------------------------------------------------

    private static void AnalyzeLoop(OperationAnalysisContext ctx)
    {
        // Report on the keyword only, so the whole loop body isn't underlined.
        // `await foreach` starts with `await`, so step past it to the loop keyword.
        var keyword = ctx.Operation.Syntax.GetFirstToken();
        if (keyword.IsKind(SyntaxKind.AwaitKeyword)) keyword = keyword.GetNextToken();
        ctx.ReportDiagnostic(Diagnostic.Create(NoLoops, keyword.GetLocation(), keyword.Text));
    }

    // FP0002 Mutation ---------------------------------------------------------

    private static void AnalyzeAssignment(OperationAnalysisContext ctx)
    {
        var op = (IAssignmentOperation)ctx.Operation;

        // `var (a, b) = t;`, `(var a, var b) = t;` and `_ = x;` bind new names. Not mutation.
        if (IsFreshBinding(op.Target)) return;

        // `new Foo { X = 1 }`, `foo with { X = 1 }` and `new { X = 1 }` build a new value.
        if (op.Parent is IObjectOrCollectionInitializerOperation or IAnonymousObjectCreationOperation) return;

        // Writing an out parameter is how a TryParse-style method returns its value.
        if (op.Target is IParameterReferenceOperation { Parameter.RefKind: RefKind.Out }
            && op.Kind == OperationKind.SimpleAssignment) return;

        // Constructors and init accessors may set their own type's members once.
        if (IsInitialisingOwnMember(op.Target, ctx.ContainingSymbol)) return;

        Report(ctx, NoMutation, op.Syntax, op.Target.Syntax.ToString());
    }

    private static void AnalyzeIncrement(OperationAnalysisContext ctx)
    {
        var op = (IIncrementOrDecrementOperation)ctx.Operation;
        Report(ctx, NoMutation, op.Syntax, op.Target.Syntax.ToString());
    }

    private static void AnalyzeEventAssignment(OperationAnalysisContext ctx)
    {
        var op = (IEventAssignmentOperation)ctx.Operation;
        Report(ctx, NoMutation, op.Syntax, op.EventReference.Syntax.ToString());
    }

    private static bool IsFreshBinding(IOperation target) => target switch
    {
        IDiscardOperation => true,
        IDeclarationExpressionOperation => true,
        ILocalReferenceOperation { IsDeclaration: true } => true,
        ITupleOperation tuple => tuple.Elements.All(IsFreshBinding),
        _ => false,
    };

    private static bool IsInitialisingOwnMember(IOperation target, ISymbol containing)
    {
        if (containing is not IMethodSymbol method) return false;
        if (target is not IMemberReferenceOperation member) return false;
        if (!SymbolEqualityComparer.Default.Equals(member.Member.ContainingType, method.ContainingType)) return false;

        var isInstanceInit = method.MethodKind == MethodKind.Constructor || method.IsInitOnly;
        var isStaticInit = method.MethodKind == MethodKind.StaticConstructor;

        return (isInstanceInit && member.Instance is IInstanceReferenceOperation)
            || (isStaticInit && member.Member.IsStatic);
    }

    // FP0003 Mutable state ----------------------------------------------------

    private static void AnalyzeField(SymbolAnalysisContext ctx)
    {
        var field = (IFieldSymbol)ctx.Symbol;
        if (field.IsImplicitlyDeclared || field.IsConst || field.IsReadOnly) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            NoMutableState, field.Locations[0], "Field", field.Name, "Mark it readonly."));
    }

    private static void AnalyzeProperty(SymbolAnalysisContext ctx)
    {
        var property = (IPropertySymbol)ctx.Symbol;
        if (property.IsImplicitlyDeclared || property.SetMethod is null || property.SetMethod.IsInitOnly) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            NoMutableState,
            property.Locations[0],
            property.IsIndexer ? "Indexer" : "Property",
            property.IsIndexer ? "this[]" : property.Name,
            "Use 'init' instead of 'set', or make it get-only."));
    }

    // FP0003 / FP0004 Types ---------------------------------------------------

    private static void AnalyzeType(SymbolAnalysisContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;
        if (type.IsImplicitlyDeclared || type.Locations.Length == 0 || !type.Locations[0].IsInSource) return;

        // `record struct P(int X)` generates settable properties.
        if (type.TypeKind == TypeKind.Struct && type.IsRecord && !type.IsReadOnly)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                NoMutableState, type.Locations[0], "Record struct", type.Name,
                "Declare it as 'readonly record struct'."));
            return;
        }

        if (type.TypeKind != TypeKind.Class || type.IsRecord || type.IsStatic) return;

        // Top-level statements compile into a hidden Program class. That's host code, not a class someone wrote.
        if (!type.GetMembers(WellKnownMemberNames.TopLevelStatementsEntryPointMethodName).IsEmpty) return;

        // Attributes have to be classes.
        if (InheritsFrom(type, "System.Attribute")) return;

        ctx.ReportDiagnostic(Diagnostic.Create(NoObjects, type.Locations[0], type.Name));
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string fullName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == fullName) return true;
        return false;
    }

    // FP0005 Creating mutable collections -------------------------------------

    // LINQ methods that build a mutable collection, and what to call instead.
    private static readonly Dictionary<string, string> MutableBuilders = new(StringComparer.Ordinal)
    {
        ["ToList"] = "ToImmutableArray() or ToImmutableList()",
        ["ToDictionary"] = "ToImmutableDictionary()",
        ["ToHashSet"] = "ToImmutableHashSet()",
    };

    private static void AnalyzeCreation(OperationAnalysisContext ctx, Dictionary<ISymbol, string> banned)
    {
        var type = ctx.Operation.Type;
        if (type is null || !banned.TryGetValue(type.OriginalDefinition, out var alternative)) return;

        if (ctx.Operation is IInvocationOperation invocation)
        {
            // Methods written in this compilation are already checked by FP0008 (their signature)
            // or FP0006 (legacy). Here we only want library calls such as Enumerable.ToList.
            if (invocation.TargetMethod.Locations.Any(l => l.IsInSource)) return;
            if (MutableBuilders.TryGetValue(invocation.TargetMethod.Name, out var builder)) alternative = builder;
        }

        Report(ctx, NoMutableTypes, ctx.Operation.Syntax, Display(type), alternative);
    }

    // FP0006 Calling legacy code directly -------------------------------------

    private static void AnalyzeBoundary(OperationAnalysisContext ctx)
    {
        var legacy = LegacyNamespaces(ctx.Options, ctx.Operation.Syntax.SyntaxTree);
        if (legacy.IsEmpty) return;

        var (_, type) = Referenced(ctx.Operation);
        if (type is null || !IsInNamespaces(type, legacy)) return;

        Report(ctx, NoDirectImperative, ctx.Operation.Syntax, type.Name);
    }

    // FP0007 Throwing ---------------------------------------------------------

    private static void AnalyzeThrow(OperationAnalysisContext ctx)
    {
        var keyword = ctx.Operation.Syntax.GetFirstToken();
        ctx.ReportDiagnostic(Diagnostic.Create(NoThrow, keyword.GetLocation()));
    }

    // FP0008 Mutable types in signatures --------------------------------------

    private static void AnalyzeSignature(SymbolAnalysisContext ctx, Dictionary<ISymbol, string> banned)
    {
        var symbol = ctx.Symbol;
        // Overrides and interface implementations can't choose their signature.
        if (symbol.IsImplicitlyDeclared || symbol.IsOverride || IsExplicitImplementation(symbol)) return;

        foreach (var (type, location) in SignatureTypes(symbol))
        {
            var hit = TypesIn(type).FirstOrDefault(t => banned.ContainsKey(t.OriginalDefinition));
            if (hit is null) continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                MutableSignature, location, Display(hit), banned[hit.OriginalDefinition]));
        }
    }

    // FP0009 Adapters leaking legacy types ------------------------------------

    private static void AnalyzeAdapterSignature(SymbolAnalysisContext ctx)
    {
        var symbol = ctx.Symbol;
        if (symbol.IsImplicitlyDeclared || symbol.DeclaredAccessibility == Accessibility.Private) return;

        // Constructors may take legacy services: that's how an adapter gets hold of them.
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor }) return;
        if (!IsInAdapter(symbol)) return;

        var tree = symbol.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree;
        if (tree is null) return;
        var legacy = LegacyNamespaces(ctx.Options, tree);
        if (legacy.IsEmpty) return;

        foreach (var (type, location) in SignatureTypes(symbol))
        {
            var hit = TypesIn(type).FirstOrDefault(t => IsInNamespaces(t, legacy));
            if (hit is null) continue;

            ctx.ReportDiagnostic(Diagnostic.Create(AdapterLeak, location, symbol.Name, hit.Name));
        }
    }

    // FP0010 async void -------------------------------------------------------

    private static void AnalyzeAsyncVoidMethod(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is IMethodSymbol { IsAsync: true, ReturnsVoid: true } method && !method.IsImplicitlyDeclared)
            ctx.ReportDiagnostic(Diagnostic.Create(AsyncVoid, method.Locations[0], method.Name));
    }

    private static void AnalyzeAsyncVoidFunction(OperationAnalysisContext ctx)
    {
        var method = ctx.Operation switch
        {
            IAnonymousFunctionOperation lambda => lambda.Symbol,
            ILocalFunctionOperation local => local.Symbol,
            _ => null,
        };
        if (method is not { IsAsync: true, ReturnsVoid: true }) return;

        var isLambda = method.MethodKind == MethodKind.AnonymousFunction;
        var location = isLambda ? ctx.Operation.Syntax.GetFirstToken().GetLocation() : method.Locations[0];
        ctx.ReportDiagnostic(Diagnostic.Create(AsyncVoid, location, isLambda ? "lambda" : method.Name));
    }

    // FP0011 Side-effecting APIs ----------------------------------------------

    private static void AnalyzeImpure(OperationAnalysisContext ctx, ImpureApis apis)
    {
        var (member, type) = Referenced(ctx.Operation);
        if (member is null || type is null || !apis.Contains(member, type)) return;

        var isWholeType = member is INamedTypeSymbol || member is IMethodSymbol { MethodKind: MethodKind.Constructor };
        Report(ctx, ImpureApi, ctx.Operation.Syntax, isWholeType ? type.Name : $"{type.Name}.{member.Name}");
    }

    private sealed class ImpureApis
    {
        private readonly HashSet<ISymbol> _types = new(SymbolEqualityComparer.Default);
        private readonly HashSet<string> _members = new(StringComparer.Ordinal);

        public static ImpureApis Create(Compilation compilation, string extra)
        {
            var apis = new ImpureApis();
            foreach (var entry in DefaultImpureApis.Concat(SplitList(extra)))
            {
                if (compilation.GetTypeByMetadataName(entry) is { } type)
                {
                    apis._types.Add(type);
                    continue;
                }

                var dot = entry.LastIndexOf('.');
                if (dot > 0 && compilation.GetTypeByMetadataName(entry.Substring(0, dot)) is { } owner)
                    apis._members.Add(Key(owner, entry.Substring(dot + 1)));
            }
            return apis;
        }

        public bool Contains(ISymbol member, INamedTypeSymbol type) =>
            _types.Contains(type.OriginalDefinition)
            || _members.Contains(Key(type.OriginalDefinition, member.Name));

        private static string Key(ITypeSymbol type, string member) => type.ToDisplayString() + "." + member;
    }

    // Exemptions --------------------------------------------------------------

    private static Action<OperationAnalysisContext> GuardOp(Action<OperationAnalysisContext> rule) => ctx =>
    {
        var legacy = LegacyNamespaces(ctx.Options, ctx.Operation.Syntax.SyntaxTree);
        if (!IsExempt(ctx.ContainingSymbol, legacy)) rule(ctx);
    };

    private static Action<SymbolAnalysisContext> GuardSymbol(Action<SymbolAnalysisContext> rule) => ctx =>
    {
        var tree = ctx.Symbol.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree;
        if (tree is null) return;
        var legacy = LegacyNamespaces(ctx.Options, tree);
        if (!IsExempt(ctx.Symbol, legacy)) rule(ctx);
    };

    private static bool IsExempt(ISymbol symbol, ImmutableArray<string> legacy) =>
        IsInAdapter(symbol) || IsInNamespaces(symbol, legacy);

    // True if the symbol, its property/event, or any enclosing type has [ImperativeAdapter].
    private static bool IsInAdapter(ISymbol? symbol)
    {
        while (symbol is not null and not INamespaceSymbol)
        {
            if (HasAdapterAttribute(symbol)) return true;
            if (symbol is IMethodSymbol { AssociatedSymbol: { } owner } && HasAdapterAttribute(owner)) return true;
            symbol = symbol.ContainingSymbol;
        }
        return false;
    }

    private static bool HasAdapterAttribute(ISymbol symbol) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.Name == AdapterAttributeName);

    private static bool IsInNamespaces(ISymbol symbol, ImmutableArray<string> namespaces)
    {
        if (namespaces.IsEmpty || symbol.ContainingNamespace is null) return false;
        var name = symbol.ContainingNamespace.ToDisplayString();
        return namespaces.Any(ns => name == ns || name.StartsWith(ns + ".", StringComparison.Ordinal));
    }

    // Helpers -----------------------------------------------------------------

    private static ImmutableArray<string> LegacyNamespaces(AnalyzerOptions options, SyntaxTree tree) =>
        SplitList(OptionValue(options, tree, LegacyNamespacesKey));

    private static string OptionValue(AnalyzerOptions options, SyntaxTree tree, string key) =>
        options.AnalyzerConfigOptionsProvider.GetOptions(tree).TryGetValue(key, out var value) ? value : "";

    private static ImmutableArray<string> SplitList(string value) =>
        value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToImmutableArray();

    private static Dictionary<ISymbol, string> Resolve(Compilation compilation, IEnumerable<(string MetadataName, string Alternative)> entries)
    {
        var map = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        foreach (var (name, alternative) in entries)
            if (compilation.GetTypeByMetadataName(name) is { } type) map[type] = alternative;
        return map;
    }

    // The member an operation uses, and the type that owns it.
    private static (ISymbol? Member, INamedTypeSymbol? Type) Referenced(IOperation operation)
    {
        ISymbol? member = operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IObjectCreationOperation creation => (ISymbol?)creation.Constructor ?? creation.Type,
            IMemberReferenceOperation reference => reference.Member,
            _ => null,
        };
        return (member, member as INamedTypeSymbol ?? member?.ContainingType);
    }

    // Every type a member exposes, with where to report it.
    private static IEnumerable<(ITypeSymbol Type, Location Location)> SignatureTypes(ISymbol symbol)
    {
        switch (symbol)
        {
            case IMethodSymbol method when method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor
                                           or MethodKind.UserDefinedOperator or MethodKind.Conversion:
                if (!method.ReturnsVoid) yield return (method.ReturnType, method.Locations[0]);
                foreach (var parameter in method.Parameters)
                    if (!IsRecordPropertyParameter(method, parameter))
                        yield return (parameter.Type, parameter.Locations.FirstOrDefault() ?? method.Locations[0]);
                break;

            case IPropertySymbol property:
                yield return (property.Type, property.Locations[0]);
                foreach (var parameter in property.Parameters)
                    yield return (parameter.Type, parameter.Locations.FirstOrDefault() ?? property.Locations[0]);
                break;

            case IFieldSymbol field:
                yield return (field.Type, field.Locations[0]);
                break;
        }
    }

    // A positional record parameter is also a property. Report it once, on the property.
    private static bool IsRecordPropertyParameter(IMethodSymbol method, IParameterSymbol parameter) =>
        method.MethodKind == MethodKind.Constructor
        && method.ContainingType.IsRecord
        && method.ContainingType.GetMembers(parameter.Name).OfType<IPropertySymbol>()
            .Any(p => p.Locations.SequenceEqual(parameter.Locations));

    // A type plus its type arguments and element types, so Task<List<T>> is caught.
    private static IEnumerable<ITypeSymbol> TypesIn(ITypeSymbol type)
    {
        yield return type;
        var children = type switch
        {
            INamedTypeSymbol named => named.TypeArguments,
            IArrayTypeSymbol array => ImmutableArray.Create(array.ElementType),
            _ => ImmutableArray<ITypeSymbol>.Empty,
        };
        foreach (var child in children)
            foreach (var nested in TypesIn(child))
                yield return nested;
    }

    private static bool IsExplicitImplementation(ISymbol symbol) => symbol switch
    {
        IMethodSymbol m => m.ExplicitInterfaceImplementations.Length > 0,
        IPropertySymbol p => p.ExplicitInterfaceImplementations.Length > 0,
        _ => false,
    };

    private static string Display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static void Report(OperationAnalysisContext ctx, DiagnosticDescriptor rule, SyntaxNode node, params object[] args) =>
        ctx.ReportDiagnostic(Diagnostic.Create(rule, node.GetLocation(), args));
}
