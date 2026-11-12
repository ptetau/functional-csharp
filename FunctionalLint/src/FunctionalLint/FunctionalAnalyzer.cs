using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
///   FP0012  don't throw away the result of a call that returns a new value
///   FP0013  switches over enums, closed types and unions must stay exhaustive
///   FP0014  records must not hold collections that compare by reference
///   FP0015  no calls that throw (First, Parse, dictionary indexers...)
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
        "{0} {1}",
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
        "{0} Use {1} instead.",
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
        "'{0}' {1}. {2}",
        "Functions that read the clock, random numbers, the console, files or the network give different answers " +
        "for the same input. Pass the value in as a parameter (e.g. 'DateTime now'), or wrap the API in an adapter. " +
        "Indented JSON options without 'NewLine' are reported too: they write Environment.NewLine.");

    public static readonly DiagnosticDescriptor IgnoredResult = Rule("FP0012",
        "Result of a pure call is ignored",
        "'{0}' returns a new value and leaves the original unchanged. Use the result instead.",
        "Immutable collections, strings, dates and LINQ never change what they're called on: 'list.Add(x)' returns a new list. " +
        "Calling one as a statement does nothing, so it's almost always a bug. Assign the result: 'var updated = list.Add(x);'.");

    public static readonly DiagnosticDescriptor ExhaustiveSwitch = Rule("FP0013",
        "Switches must stay exhaustive",
        "{0} {1}",
        "The compiler checks that a switch expression over an enum, a closed type or a union handles every case, " +
        "so adding a case is a compile error until each switch handles it. A catch-all arm or a switch statement turns " +
        "that check off.");

    public static readonly DiagnosticDescriptor ReferenceEqualityInRecord = Rule("FP0014",
        "Records must not hold collections that compare by reference",
        "'{0}' compares by reference, so equal '{1}' records won't be equal. Use {2} instead.",
        "Records promise value equality, but ImmutableArray, arrays, lists and dictionaries compare by reference, so a record " +
        "holding one is never equal to a copy of itself. Use a collection marked [ValueEquality] (such as ValueList<T>), " +
        "or write the record's Equals yourself.");

    public static readonly DiagnosticDescriptor ThrowingCall = Rule("FP0015",
        "Calls that throw are not allowed",
        "'{0}' throws when {1}. Use {2} instead.",
        "FP0007 stops 'throw', but many library calls throw too: First on an empty sequence, Parse on bad input, a " +
        "dictionary indexer on a missing key. Use the form that returns a value you can check, and turn failure into a " +
        "Result or Option. Adapters may call them.");

    public static readonly DiagnosticDescriptor RuleSwitchedOff = Rule("FP0016",
        "Checks must stay on",
        "'{0}' {1}. {2}",
        "A functional core can't turn checks off: no '#pragma warning disable', [SuppressMessage] or '#nullable disable'. " +
        "The gate (heal.sh --check, the agent hook, CI) also reports what the build can't: a rule lowered by NoWarn or " +
        "config, nullable checking or analyzers turned off, code marked as generated ([GeneratedCode], '#line hidden', " +
        "a generated file), a second policy file, or a policy core that no longer loads FunctionalLint. " +
        "functional_lint.may_lower lists the exceptions the project owner allows.");

    public static readonly DiagnosticDescriptor AdapterPlacement = Rule("FP0017",
        "Adapters only where the policy allows them",
        "'{0}' is an [ImperativeAdapter]{1}. Move it {2}, or pass the value in.",
        "[ImperativeAdapter] switches every rule off for what it marks, so where adapters may live is the project " +
        "owner's decision: functional_lint.adapter_namespaces (set by functionallint.policy). Off when that key isn't set.");

    public static readonly DiagnosticDescriptor ReferenceOutsidePolicy = Rule("FP0018",
        "Functional cores reference only allowed libraries",
        "'{0}' isn't on this core's list of allowed references. Move the code that uses it to a shell project.",
        "A core that can't reference a database client, logger or HTTP library can't call one. A core may reference " +
        "the .NET base library and what functional_lint.allowed_references lists (set by functionallint.policy). " +
        "Off when that key isn't set.");

    public const string HelpLinkBase = "https://github.com/ptetau/functional-csharp/blob/main/FunctionalLint/docs/rules/";

    private static DiagnosticDescriptor Rule(string id, string title, string message, string description) =>
        new(id, title, message, "Functional", DiagnosticSeverity.Error, isEnabledByDefault: true,
            description: description, helpLinkUri: HelpLinkBase + id + ".md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        NoLoops, NoMutation, NoMutableState, NoObjects, NoMutableTypes, NoDirectImperative,
        NoThrow, MutableSignature, AdapterLeak, AsyncVoid, ImpureApi, IgnoredResult, ExhaustiveSwitch,
        ReferenceEqualityInRecord, ThrowingCall, RuleSwitchedOff, AdapterPlacement, ReferenceOutsidePolicy);

    // Configuration -----------------------------------------------------------

    // Matched by name, so consumers define the attribute themselves (see README).
    private const string AdapterAttributeName = "ImperativeAdapterAttribute";

    // .editorconfig / .globalconfig keys. Both take comma-separated lists.
    private const string LegacyNamespacesKey = "functional_lint.legacy_namespaces";
    private const string ImpureApisKey = "functional_lint.impure_apis";
    // Policy keys: unset means no restriction. Set (even to "none") means only what's listed.
    public const string AdapterNamespacesKey = "functional_lint.adapter_namespaces";
    public const string AllowedReferencesKey = "functional_lint.allowed_references";
    public const string MayLowerKey = "functional_lint.may_lower";

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
        ("System.Collections.Concurrent.ConcurrentQueue`1", "ImmutableQueue<T>"),
        ("System.Collections.Concurrent.ConcurrentStack`1", "ImmutableStack<T>"),
        ("System.Collections.Concurrent.ConcurrentBag`1", "ImmutableArray<T>"),
        ("System.Collections.Generic.PriorityQueue`2", "ImmutableSortedSet<T>, or an ImmutableList kept in order"),
        ("System.Collections.ObjectModel.Collection`1", "ImmutableArray<T>"),
        ("System.Collections.ObjectModel.ObservableCollection`1", "ImmutableArray<T>"),
        ("System.Collections.ArrayList", "ImmutableArray<T>"),
        ("System.Collections.Hashtable", "ImmutableDictionary<TKey, TValue>"),
        ("System.Text.StringBuilder", "string interpolation, string.Join or string.Concat"),
        ("System.IO.MemoryStream", "a byte array built with LINQ, or an [ImperativeAdapter]"),
        ("System.IO.StringWriter", "string interpolation or string.Join"),
        ("System.Text.Json.Utf8JsonWriter", "JsonSerializer.Serialize, or an [ImperativeAdapter]"),
    };

    // Signatures get the read-only interface first: it's what the FP0008 fix writes, and callers holding
    // a list can still pass it.
    private static readonly (string MetadataName, string Alternative)[] SignatureAlternatives =
    {
        ("System.Collections.Generic.List`1", "IReadOnlyList<T> or ImmutableArray<T>"),
        ("System.Collections.Generic.Dictionary`2", "IReadOnlyDictionary<TKey, TValue>"),
        ("System.Collections.Generic.HashSet`1", "IReadOnlySet<T> or ImmutableHashSet<T>"),
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
        "System.Guid.NewGuid", "System.Guid.CreateVersion7",
        "System.IO.Path.GetTempFileName", "System.IO.Path.GetRandomFileName",
        "System.Random",
        "System.Security.Cryptography.RandomNumberGenerator",
        "System.Console",
        "System.Environment",
        "System.IO.File", "System.IO.Directory", "System.IO.FileInfo", "System.IO.DirectoryInfo",
        "System.IO.FileStream", "System.IO.StreamReader", "System.IO.StreamWriter",
        "System.Net.Http.HttpClient",
        "System.Diagnostics.Process", "System.Diagnostics.Stopwatch",
        "System.Threading.Thread.Sleep", "System.Threading.Tasks.Task.Delay",
        "System.Globalization.CultureInfo.CurrentCulture", "System.Globalization.CultureInfo.CurrentUICulture",
        "System.TimeZoneInfo.Local", "System.IO.Path.GetFullPath", "System.IO.Path.GetTempPath",
        "System.Linq.ParallelEnumerable", "System.Threading.Tasks.Parallel",
    };

    // Not side effects as such, but still hidden inputs: the answer depends on where or how the code runs.
    private static readonly string[] AmbientApis =
    {
        "System.Environment", "System.Globalization.CultureInfo.CurrentCulture", "System.Globalization.CultureInfo.CurrentUICulture",
        "System.TimeZoneInfo.Local", "System.IO.Path.GetFullPath", "System.IO.Path.GetTempPath",
    };

    private static readonly string[] NondeterministicApis = { "System.Linq.ParallelEnumerable", "System.Threading.Tasks.Parallel" };

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

        // Runs only on adapters, so it is deliberately not guarded.
        context.RegisterSymbolAction(AnalyzeAdapterSignature, SymbolKind.Method, SymbolKind.Property, SymbolKind.Field);
        // Policy rules: about adapters and the project itself, so not guarded either.
        context.RegisterSyntaxNodeAction(AnalyzeAdapterPlacement, SyntaxKind.Attribute);
        context.RegisterCompilationAction(AnalyzeReferences);
        context.RegisterSemanticModelAction(ctx =>
        {
            var tree = ctx.SemanticModel.SyntaxTree;
            var mayLower = PolicyList(ctx.Options.AnalyzerConfigOptionsProvider.GetOptions(tree), MayLowerKey) ?? ImmutableArray<string>.Empty;
            foreach (var found in SwitchOffs(tree, ctx.SemanticModel, mayLower, ctx.CancellationToken)) ctx.ReportDiagnostic(found);
        });

        context.RegisterCompilationStartAction(start =>
        {
            // The guard skips adapters and legacy namespaces. It caches per compilation, so that work isn't
            // repeated for each of the many operations every rule looks at.
            var guard = new Guard();
            var GuardOp = guard.Op;
            var GuardSymbol = guard.Symbol;

            start.RegisterOperationAction(GuardOp(AnalyzeLoop), OperationKind.Loop);
            start.RegisterOperationAction(GuardOp(AnalyzeIndentedWriter), OperationKind.ObjectCreation);
            start.RegisterOperationAction(GuardOp(AnalyzeReplaceLineEndings), OperationKind.Invocation);
            start.RegisterOperationAction(GuardOp(AnalyzeGoto), OperationKind.Branch);
            start.RegisterOperationAction(GuardOp(AnalyzeLoopCall), OperationKind.Invocation);
            start.RegisterOperationAction(GuardOp(AnalyzeRefArgument), OperationKind.Argument);
            start.RegisterOperationAction(GuardOp(AnalyzeMutatingCall), OperationKind.Invocation);
            start.RegisterOperationAction(GuardOp(AnalyzeThrowingCall), OperationKind.Invocation, OperationKind.PropertyReference);
            var valueTypes = new Lazy<(string? List, string? Map)>(() => FindValueEqualityTypes(start.Compilation));
            start.RegisterSymbolAction(GuardSymbol(ctx => AnalyzeRecordEquality(ctx, valueTypes)), SymbolKind.NamedType);
            start.RegisterOperationAction(GuardOp(AnalyzeAssignment),
                OperationKind.SimpleAssignment,
                OperationKind.CompoundAssignment,
                OperationKind.CoalesceAssignment,
                OperationKind.DeconstructionAssignment);
            start.RegisterOperationAction(GuardOp(AnalyzeIncrement), OperationKind.Increment, OperationKind.Decrement);
            start.RegisterOperationAction(GuardOp(AnalyzeEventAssignment), OperationKind.EventAssignment);
            start.RegisterOperationAction(GuardOp(AnalyzeBoundary), ReferenceKinds);
            start.RegisterOperationAction(GuardOp(AnalyzeThrow), OperationKind.Throw);
            start.RegisterOperationAction(GuardOp(AnalyzeIgnoredResult), OperationKind.ExpressionStatement);
            start.RegisterOperationAction(GuardOp(AnalyzeSwitchExpression), OperationKind.SwitchExpression);
            start.RegisterOperationAction(GuardOp(AnalyzeSwitchStatement), OperationKind.Switch);
            start.RegisterOperationAction(GuardOp(AnalyzeAsyncVoidFunction),
                OperationKind.AnonymousFunction, OperationKind.LocalFunction);

            start.RegisterSymbolAction(GuardSymbol(AnalyzeField), SymbolKind.Field);
            start.RegisterSymbolAction(GuardSymbol(AnalyzeProperty), SymbolKind.Property);
            start.RegisterSymbolAction(GuardSymbol(AnalyzeType), SymbolKind.NamedType);
            start.RegisterSymbolAction(GuardSymbol(AnalyzeAsyncVoidMethod), SymbolKind.Method);

            var compilation = start.Compilation;
            var mutableTypes = Resolve(compilation, MutableTypes);
            var inSignatures = Resolve(compilation, MutableTypes.Concat(MutableInterfaces).Concat(SignatureAlternatives));
            var mutableInterfaces = Resolve(compilation, MutableInterfaces);
            var impureByConfig = new ConcurrentDictionary<string, ImpureApis>(StringComparer.Ordinal);

            start.RegisterOperationAction(
                GuardOp(ctx => AnalyzeCreation(ctx, mutableTypes)),
                OperationKind.ObjectCreation, OperationKind.CollectionExpression, OperationKind.Invocation);

            start.RegisterOperationAction(
                GuardOp(ctx => AnalyzeLocalType(ctx, mutableInterfaces)),
                OperationKind.VariableDeclaration);

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

    private static void AnalyzeGoto(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IBranchOperation { BranchKind: BranchKind.GoTo } op) return;
        var keyword = op.Syntax.GetFirstToken();
        ctx.ReportDiagnostic(Diagnostic.Create(NoLoops, keyword.GetLocation(), keyword.Text));
    }

    // `xs.ForEach(x => ...)` (List, ImmutableList, Array, Parallel) is a loop written as a method call.
    private static void AnalyzeLoopCall(OperationAnalysisContext ctx)
    {
        var op = (IInvocationOperation)ctx.Operation;
        if (op.TargetMethod.Name != "ForEach" || op.TargetMethod.Locations.Any(l => l.IsInSource)) return;
        if (!op.TargetMethod.Parameters.Any(p => p.Type.TypeKind == TypeKind.Delegate)) return;

        var name = op.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } ? access.Name : op.Syntax;
        ctx.ReportDiagnostic(Diagnostic.Create(NoLoops, name.GetLocation(), "ForEach"));
    }

    // FP0002 Mutation ---------------------------------------------------------

    private const string NewNameRemedy = "Bind a new name or use a 'with' expression instead.";

    // A loop's own variables (`i++`, `total += x`) are reported once, as FP0001 on the loop: rewriting the loop
    // removes them, and "bind a new name" is the wrong advice inside a loop.
    private static bool IsInsideLoop(IOperation op)
    {
        for (var current = op.Parent; current is not null; current = current.Parent)
            if (current is ILoopOperation) return true;
        return false;
    }

    private static void ReportChange(OperationAnalysisContext ctx, SyntaxNode at, IOperation target)
    {
        var (what, remedy) = target is IArrayElementReferenceOperation
            ? ($"'{target.Syntax}' is changed here.", "Build a new array with Select(...).ToArray() instead.")
            : ($"'{target.Syntax}' is changed here.", NewNameRemedy);
        Report(ctx, NoMutation, at, what, remedy);
    }

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
        if (IsInsideLoop(op)) return;

        ReportChange(ctx, op.Syntax, op.Target);
    }

    private static void AnalyzeIncrement(OperationAnalysisContext ctx)
    {
        var op = (IIncrementOrDecrementOperation)ctx.Operation;
        if (IsInsideLoop(op)) return;
        ReportChange(ctx, op.Syntax, op.Target);
    }

    private static void AnalyzeEventAssignment(OperationAnalysisContext ctx)
    {
        var op = (IEventAssignmentOperation)ctx.Operation;
        Report(ctx, NoMutation, op.Syntax, $"'{op.EventReference.Syntax}' is changed here.", NewNameRemedy);
    }

    // `int.TryParse(s, out existing)` and `Interlocked.Increment(ref n)` overwrite a variable that already exists.
    // `out var n` and `out _` bind a new name, and passing your own out parameter on is how TryX methods chain.
    private static void AnalyzeRefArgument(OperationAnalysisContext ctx)
    {
        var op = (IArgumentOperation)ctx.Operation;
        if (op.Parameter is not { RefKind: RefKind.Out or RefKind.Ref } parameter) return;
        // Only a variable you can see can be overwritten. (`string.Create(culture, $"...")` passes a
        // compiler-made interpolation handler by ref: nothing of yours changes.)
        if (op.Value is not (ILocalReferenceOperation or IFieldReferenceOperation or IParameterReferenceOperation
            or IArrayElementReferenceOperation)) return;
        if (IsFreshBinding(op.Value)) return;
        if (op.Value is IParameterReferenceOperation { Parameter.RefKind: RefKind.Out }) return;
        if (IsInsideLoop(op)) return;

        var remedy = parameter.RefKind == RefKind.Out
            ? "Use 'out var' to bind a new name instead."
            : "Pass the value in and use the result instead.";
        Report(ctx, NoMutation, op.Syntax, $"'{op.Value.Syntax}' is changed here.", remedy);
    }

    // Library calls that change their argument in place, or wrap a mutable array as an "immutable" one.
    private static readonly string[] MutatingApis =
    {
        "System.Array.Fill", "System.Array.Clear", "System.Array.Sort", "System.Array.Reverse", "System.Array.Copy",
        "System.Array.Resize", "System.Array.ConstrainedCopy",
        "System.Span<T>.Fill", "System.Span<T>.Clear", "System.Span<T>.CopyTo", "System.Span<T>.TryCopyTo",
        "System.MemoryExtensions.Sort", "System.MemoryExtensions.Reverse",
        "System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray",
    };

    private static void AnalyzeMutatingCall(OperationAnalysisContext ctx)
    {
        var method = ((IInvocationOperation)ctx.Operation).TargetMethod;
        var key = method.ContainingType.OriginalDefinition.ToDisplayString() + "." + method.Name;
        if (!MutatingApis.Contains(key)) return;

        var (what, remedy) = method.ContainingType.Name == "ImmutableCollectionsMarshal"
            ? ($"'{method.Name}' makes an 'immutable' array that its owner can still change.", "Use ToImmutableArray() instead.")
            : ($"'{method.ContainingType.Name}.{method.Name}' changes its argument in place.", "Build a new value with LINQ instead.");
        Report(ctx, NoMutation, ctx.Operation.Syntax, what, remedy);
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

        // `record struct P(int X)`: reported once, on the struct. Declaring it readonly fixes every property.
        if (property.ContainingType is { TypeKind: TypeKind.Struct, IsRecord: true, IsReadOnly: false }
            && property.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is ParameterSyntax)) return;

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
        ["CreateBuilder"] = "Select(...).ToImmutableArray()",
        ["ToBuilder"] = "the immutable collection's own Add/SetItem/Remove, using each result",
    };

    private static void AnalyzeCreation(OperationAnalysisContext ctx, Dictionary<ISymbol, string> known)
    {
        var type = ctx.Operation.Type;
        if (type is null || MutableAlternative(type, known) is not { } alternative) return;

        if (ctx.Operation is IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            // Methods written in this compilation are already checked by FP0008 (their signature)
            // or FP0006 (legacy). Here we only want library calls such as Enumerable.ToList.
            if (method.Locations.Any(l => l.IsInSource)) return;
            // `sb.Append(x)` returns the builder it was called on: that builder was reported where it was made.
            if (!method.IsStatic && !method.IsExtensionMethod
                && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, type.OriginalDefinition)) return;
            if (MutableBuilders.TryGetValue(method.Name, out var builder)) alternative = builder;
        }

        Report(ctx, NoMutableTypes, ctx.Operation.Syntax, Display(type), alternative);
    }

    private static readonly string[] CollectionNamespaces =
    {
        "System.Collections", "System.Collections.Generic", "System.Collections.Concurrent", "System.Collections.ObjectModel",
    };

    // What to use instead of a mutable type, or null if it isn't one. Known types have tailored advice. Beyond those,
    // anything in the standard collection namespaces that can be added to is mutable, and so are the immutable
    // collections' builders (ImmutableArray<T>.Builder and friends).
    private static string? MutableAlternative(ITypeSymbol type, Dictionary<ISymbol, string> known)
    {
        if (known.TryGetValue(type.OriginalDefinition, out var alternative)) return alternative;
        if (type is not INamedTypeSymbol named || type.TypeKind == TypeKind.Interface) return null;

        if (named.Name == "Builder" && named.ContainingType?.ContainingNamespace?.ToDisplayString() == "System.Collections.Immutable")
            return "Select(...).ToImmutableArray()";

        var ns = named.ContainingNamespace?.ToDisplayString() ?? "";
        if (!CollectionNamespaces.Contains(ns) || named.Name.StartsWith("ReadOnly", StringComparison.Ordinal)) return null;

        var addable = named.AllInterfaces.Any(i =>
            i.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.ICollection<T>" or "System.Collections.IList" or "System.Collections.IDictionary");
        return addable ? "a type from System.Collections.Immutable" : null;
    }

    // `IList<int> xs = ...` declares a local that can be changed. Concrete types such as List<T> are
    // caught where they're created (above), so only the interfaces need checking here.
    private static void AnalyzeLocalType(OperationAnalysisContext ctx, Dictionary<ISymbol, string> banned)
    {
        var op = (IVariableDeclarationOperation)ctx.Operation;
        if (op.Syntax is not VariableDeclarationSyntax { Type: { IsVar: false } typeSyntax }) return;
        if (op.Declarators.FirstOrDefault()?.Symbol.Type is not { } type) return;

        var hit = TypesIn(type).FirstOrDefault(t => banned.ContainsKey(t.OriginalDefinition));
        if (hit is null) return;

        Report(ctx, NoMutableTypes, typeSyntax, Display(hit), banned[hit.OriginalDefinition]);
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

            var problem = symbol is IFieldSymbol
                ? $"'{Display(hit)}' in a field can be changed by any code that can see it."
                : $"'{Display(hit)}' lets callers change the collection.";
            ctx.ReportDiagnostic(Diagnostic.Create(MutableSignature, location, problem, banned[hit.OriginalDefinition]));
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

    // FP0013 Exhaustive switches ---------------------------------------------

    private static void AnalyzeSwitchExpression(OperationAnalysisContext ctx)
    {
        var op = (ISwitchExpressionOperation)ctx.Operation;
        if (op.Value.Type is not { } type || CheckedKind(type) is null) return;

        // An unguarded arm that matches anything left: `_`, `var x`, the input's own type, `{ }` or `not null`.
        var underlying = Unwrapped(type);
        bool IsInput(ITypeSymbol? matched) =>
            SymbolEqualityComparer.Default.Equals(matched, type) || SymbolEqualityComparer.Default.Equals(matched, underlying);
        var catchAll = op.Arms.FirstOrDefault(arm => arm.Guard is null && arm.Pattern switch
        {
            IDiscardPatternOperation => true,
            IDeclarationPatternOperation { MatchesNull: true } => true,
            IDeclarationPatternOperation declaration => IsInput(declaration.MatchedType),
            ITypePatternOperation typePattern => IsInput(typePattern.MatchedType),
            IRecursivePatternOperation { DeconstructionSubpatterns.IsEmpty: true, PropertySubpatterns.IsEmpty: true } recursive =>
                recursive.MatchedType is null || IsInput(recursive.MatchedType),
            INegatedPatternOperation { Pattern: IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } } } => true,
            _ => false,
        });
        if (catchAll is null) return;

        Report(ctx, ExhaustiveSwitch, catchAll.Pattern.Syntax,
            $"'{catchAll.Pattern.Syntax}' matches every case of '{Display(type)}' that isn't listed, so a new case won't be an error.",
            "List each case instead.");
    }

    private static void AnalyzeSwitchStatement(OperationAnalysisContext ctx)
    {
        var op = (ISwitchOperation)ctx.Operation;
        if (op.Value.Type is not { } type || CheckedKind(type) is not { } kind) return;

        var keyword = op.Syntax.GetFirstToken();
        ctx.ReportDiagnostic(Diagnostic.Create(ExhaustiveSwitch, keyword.GetLocation(),
            $"A switch statement over the {kind} '{Display(type)}' isn't checked for missing cases.",
            "Use a switch expression instead."));
    }

    // `Colour?` is checked like `Colour`.
    private static ITypeSymbol Unwrapped(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    // Types whose switch expressions the compiler checks for missing cases, or null.
    private static string? CheckedKind(ITypeSymbol type) => Unwrapped(type) switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Enum } e when !e.GetAttributes().Any(a => a.AttributeClass?.Name == "FlagsAttribute") => "enum",
        var t when t.AllInterfaces.Any(i => i.ToDisplayString() == "System.Runtime.CompilerServices.IUnion") => "union",
        var t when IsClosed(t) => "closed type",
        _ => null,
    };

    // C# 15 `closed` types. In source the modifier is visible in the syntax. For compiled types only the compiler
    // knows, through ITypeSymbol.IsClosed, which is still marked experimental. This analyzer is built against an
    // older Roslyn, so it reads that property by reflection and quietly does without it if it ever goes away.
    private static readonly PropertyInfo? IsClosedProperty =
        typeof(ITypeSymbol).GetRuntimeProperties().FirstOrDefault(p => p.Name == "IsClosed" && p.PropertyType == typeof(bool));

    private static bool IsClosed(ITypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        if (definition.DeclaringSyntaxReferences.Any(r =>
                r.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(m => m.Text == "closed")))
            return true;

        try
        {
            return IsClosedProperty?.GetValue(definition) is true;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
    }

    // FP0014 Collections in records ---------------------------------------------

    private static void AnalyzeRecordEquality(SymbolAnalysisContext ctx, Lazy<(string? List, string? Map)> valueTypes)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;
        if (!type.IsRecord || type.IsImplicitlyDeclared) return;

        // A private nested record (typically a fold's running state) is only compared where you can see it.
        // Leave it alone: switching it to an array-backed ValueList can make the fold O(n²).
        if (type.DeclaredAccessibility == Accessibility.Private && type.ContainingType is not null) return;

        // A record that writes its own Equals has decided what equality means.
        if (type.GetMembers("Equals").OfType<IMethodSymbol>().Any(m =>
                !m.IsImplicitlyDeclared && m.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type.OriginalDefinition, type.OriginalDefinition)))
            return;

        // Only what the record stores takes part in its equality: fields, and properties with a backing field
        // (auto-properties, positional parameters, `field`). A computed property like `Count => Items.Length` doesn't.
        var backed = type.GetMembers().OfType<IFieldSymbol>()
            .Select(f => f.AssociatedSymbol).Where(s => s is not null).ToImmutableHashSet(SymbolEqualityComparer.Default);

        var members = type.GetMembers()
            .Where(m => !m.IsStatic && !m.IsImplicitlyDeclared && (m is not IPropertySymbol || backed.Contains(m)))
            .Select(m => m switch
            {
                IPropertySymbol p => (Symbol: (ISymbol)p, p.Type),
                IFieldSymbol f => (Symbol: f, f.Type),
                _ => (Symbol: m, Type: (ITypeSymbol?)null),
            })
            .Where(m => m.Type is not null && ComparesByReference(m.Type));

        foreach (var (member, memberType) in members)
        {
            var isMap = memberType!.AllInterfaces.Concat(memberType is INamedTypeSymbol n ? [n] : [])
                .Any(i => i.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
                    or "System.Collections.Generic.IDictionary<TKey, TValue>");
            // Suggest a type the project can actually use; without one, the rule's docs show one to copy.
            var suggestion = (isMap ? valueTypes.Value.Map : valueTypes.Value.List) ?? "a [ValueEquality] collection";
            ctx.ReportDiagnostic(Diagnostic.Create(ReferenceEqualityInRecord, member.Locations[0], Display(memberType), type.Name,
                suggestion));
        }
    }

    // The [ValueEquality] list and map types this compilation can see, in its own source or a non-framework reference.
    // Names named ValueList / ValueMap win; otherwise any marked type with one (list) or two (map) type parameters.
    private static (string? List, string? Map) FindValueEqualityTypes(Compilation compilation)
    {
        static bool Framework(IAssemblySymbol a) =>
            a.Name.StartsWith("System", StringComparison.Ordinal) || a.Name.StartsWith("Microsoft", StringComparison.Ordinal)
            || a.Name is "netstandard" or "mscorlib" or "WindowsBase";

        static IEnumerable<INamedTypeSymbol> TypesIn(INamespaceSymbol ns) =>
            ns.GetTypeMembers().Concat(ns.GetNamespaceMembers().SelectMany(TypesIn));

        var marked = new[] { compilation.Assembly }
            .Concat(compilation.SourceModule.ReferencedAssemblySymbols.Where(a => !Framework(a)))
            .SelectMany(a => TypesIn(a.GlobalNamespace))
            .Where(t => t.DeclaredAccessibility == Accessibility.Public || SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly))
            .Where(t => t.GetAttributes().Any(a => a.AttributeClass?.Name == "ValueEqualityAttribute"))
            .ToList();

        string? Pick(string name, int arity) =>
            (marked.FirstOrDefault(t => t.Name == name && t.Arity == arity) ?? marked.FirstOrDefault(t => t.Arity == arity))
                ?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        return (Pick("ValueList", 1), Pick("ValueMap", 2));
    }

    // Collections compare by reference unless their type says otherwise with [ValueEquality] (matched by name,
    // like [ImperativeAdapter]). Strings are sequences too, but compare by value.
    private static bool ComparesByReference(ITypeSymbol type) =>
        type.SpecialType != SpecialType.System_String
        && (type is IArrayTypeSymbol
            || type.SpecialType == SpecialType.System_Collections_IEnumerable
            || type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable))
        && !type.GetAttributes().Any(a => a.AttributeClass?.Name == "ValueEqualityAttribute");

    // FP0015 Calls that throw --------------------------------------------------

    private static void AnalyzeThrowingCall(OperationAnalysisContext ctx)
    {
        var (name, when, instead) = ctx.Operation switch
        {
            IInvocationOperation invocation => ThrowingMethod(invocation),
            IPropertyReferenceOperation property => ThrowingIndexer(property),
            _ => default,
        };
        if (name is null) return;
        Report(ctx, ThrowingCall, ctx.Operation.Syntax, name, when!, instead!);
    }

    private static (string? Name, string? When, string? Instead) ThrowingMethod(IInvocationOperation op)
    {
        var method = op.TargetMethod;
        if (method.Locations.Any(l => l.IsInSource)) return default;

        var owner = method.ContainingType.OriginalDefinition.ToDisplayString();
        var name = method.Name;
        var returnsPlainValue = method.ReturnType is { IsValueType: true } r && r.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

        if (owner is "System.Linq.Enumerable" or "System.Linq.ImmutableArrayExtensions" or "System.Linq.Queryable")
        {
            return name switch
            {
                "First" when (!IsNonEmpty(op) || method.Parameters.Length > 1) && !IsUnboundedSearch(op) =>
                    (name, "nothing matches", "FirstOrDefault, or a list pattern such as 'is [var first, ..]'"),
                "Last" when !IsNonEmpty(op) || method.Parameters.Length > 1 =>
                    (name, "nothing matches", "LastOrDefault, or a list pattern such as 'is [.., var last]'"),
                "Single" or "SingleOrDefault" => (name, "there's more than one match", "a list pattern such as 'is [var only]'"),
                "ElementAt" => (name, "the index is out of range", "ElementAtOrDefault"),
                "Min" or "Max" or "Average" when returnsPlainValue && !IsNonEmpty(op) =>
                    (name, "the sequence is empty", "DefaultIfEmpty(fallback) before it"),
                "Aggregate" when method.Parameters.Length == 2 && !IsNonEmpty(op) => (name, "the sequence is empty", "Aggregate with a seed"),
                "ToDictionary" when !HasUniqueKeys(op) => (name, "two items have the same key", "GroupBy on the key first, or ToLookup"),
                _ => default,
            };
        }

        if (owner is "System.Collections.Immutable.ImmutableDictionary" or "System.Collections.Immutable.ImmutableSortedDictionary"
            && name.StartsWith("ToImmutable", StringComparison.Ordinal) && !HasUniqueKeys(op) && !CopiesADictionary(op))
            return (name, "two items have the same key", "GroupBy on the key first, or ToLookup");

        if (owner is "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>"
                or "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>" && name == "Add"
            && !(op.Instance is { } dictionary && op.Arguments.FirstOrDefault() is { } addedKey
                 && IsGuardedByContainsKey(op, dictionary, addedKey.Value, keyPresent: false)))
            return ($"{method.ContainingType.Name}.Add", "the key is already there with a different value", "SetItem");

        if (method.IsStatic && name is "Parse" or "ParseExact"
            && method.ContainingType.ContainingNamespace?.ToDisplayString() == "System")
            return ($"{Display(method.ContainingType)}.{name}", "the text isn't valid", $"{Display(method.ContainingType)}.TryParse");

        if (owner == "System.Convert" && name.StartsWith("To", StringComparison.Ordinal) && name != "ToString"
            && method.Parameters.FirstOrDefault()?.Type.SpecialType == SpecialType.System_String)
            return ($"Convert.{name}", "the text isn't valid", "the target type's TryParse");

        return default;
    }

    // `d.ContainsKey(k) ? d[k] : 0` and `d.ContainsKey(k) ? d : d.Add(k, v)` can't throw: the check guards the call.
    // Recognised in ?: and if (both are conditional operations), and on the right of && and ||.
    // The dictionary and key are matched by their source text.
    private static bool IsGuardedByContainsKey(IOperation op, IOperation dictionary, IOperation key, bool keyPresent)
    {
        bool Checks(IOperation condition, bool whenTrue) => condition switch
        {
            IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } not => Checks(not.Operand, !whenTrue),
            IInvocationOperation { TargetMethod.Name: "ContainsKey" } contains =>
                whenTrue == keyPresent
                && contains.Instance?.Syntax.ToString() == dictionary.Syntax.ToString()
                && contains.Arguments.FirstOrDefault()?.Value.Syntax.ToString() == key.Syntax.ToString(),
            _ => false,
        };

        for (IOperation child = op, parent = op.Parent!; parent is not null; child = parent, parent = parent.Parent!)
        {
            var guarded = parent switch
            {
                IConditionalOperation c when c.WhenTrue == child => Checks(c.Condition, whenTrue: true),
                IConditionalOperation c when c.WhenFalse == child => Checks(c.Condition, whenTrue: false),
                IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } b when b.RightOperand == child =>
                    Checks(b.LeftOperand, whenTrue: true),
                IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } b when b.RightOperand == child =>
                    Checks(b.LeftOperand, whenTrue: false),
                _ => false,
            };
            if (guarded) return true;
        }
        return false;
    }

    // `xs.GroupBy(k).ToDictionary(g => g.Key, ...)` can't have duplicate keys, and `xs.DefaultIfEmpty(0).Max()`
    // can't be empty: FP0015's own advice has been followed.
    // Keys are unique when the source is groups or distinct items, seen through steps that keep items unique
    // (filtering, ordering, copying into an array or list) and through a local set to such a sequence:
    // `var bySku = rows.GroupBy(r => r.Sku).ToImmutableArray(); ... bySku.ToDictionary(g => g.Key, ...)`.
    private static bool HasUniqueKeys(IInvocationOperation op) =>
        Origin(op.Instance ?? op.Arguments.FirstOrDefault()?.Value) is IInvocationOperation call
        && call.TargetMethod.Name is "GroupBy" or "Distinct" or "DistinctBy" or "ToLookup";

    private static readonly HashSet<string> KeepsItemsUnique = new(StringComparer.Ordinal)
    {
        "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Order", "OrderDescending", "Reverse",
        "Skip", "Take", "SkipWhile", "TakeWhile", "SkipLast", "TakeLast", "AsEnumerable", "AsReadOnly",
        "ToArray", "ToList", "ToImmutableArray", "ToImmutableList", "ToValueList", "ToHashSet", "ToImmutableHashSet",
    };

    // Where a sequence came from: unwraps conversions, unique-preserving steps and locals (a core can't reassign
    // one, so its initializer is its value). Stops after a few steps, so it stays cheap.
    private static IOperation? Origin(IOperation? source)
    {
        for (var step = 0; step < 12 && source is not null; step++)
        {
            switch (source)
            {
                case IConversionOperation conversion:
                    source = conversion.Operand;
                    break;
                case IInvocationOperation call when KeepsItemsUnique.Contains(call.TargetMethod.Name):
                    source = call.Instance ?? call.Arguments.FirstOrDefault()?.Value;
                    break;
                case ILocalReferenceOperation local
                    when local.Local.DeclaringSyntaxReferences.Length == 1
                         && local.Local.DeclaringSyntaxReferences[0].GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: { } value }
                         && local.SemanticModel is { } model
                         && value.SyntaxTree == model.SyntaxTree:
                    source = model.GetOperation(value);
                    break;
                default:
                    return source;
            }
        }
        return source;
    }

    // `Enumerable.Range(n, int.MaxValue - n).First(...)` (or Repeat) is a search that runs until it finds something:
    // "the first free slug suffix". It can't run out in practice, so it isn't reported.
    private static bool IsUnboundedSearch(IInvocationOperation op)
    {
        var source = op.Arguments.FirstOrDefault()?.Value;
        while (source is IConversionOperation conversion) source = conversion.Operand;
        while (source is IInvocationOperation { TargetMethod.Name: "Select" or "Where" } step)
        {
            source = step.Arguments.FirstOrDefault()?.Value;
            while (source is IConversionOperation inner) source = inner.Operand;
        }

        return source is IInvocationOperation generator
            && (generator.TargetMethod is { Name: "From", ContainingType.Name: "Sequences" }
                || generator.TargetMethod is { Name: "Range" or "Repeat", ContainingType.Name: "Enumerable" }
                   && generator.Arguments.Length == 2
                   && generator.Arguments[1].Value.Syntax.ToString().Contains("int.MaxValue"));
    }

    // Sequences that can't be empty: a group from GroupBy, or anything after Append, Prepend or DefaultIfEmpty.
    private static bool IsNonEmpty(IInvocationOperation op)
    {
        var source = op.Arguments.FirstOrDefault()?.Value;
        while (source is IConversionOperation conversion) source = conversion.Operand;
        return SourceIs(op, "DefaultIfEmpty", "Append", "Prepend")
            || source?.Type is { } type && type.AllInterfaces.Concat(type is INamedTypeSymbol n ? [n] : [])
                .Any(i => i.OriginalDefinition.ToDisplayString() == "System.Linq.IGrouping<TKey, TElement>");
    }

    // `readOnlyDictionary.ToImmutableDictionary()` copies keys that are already unique, unless a new key selector
    // or comparer could make two of them collide.
    private static bool CopiesADictionary(IInvocationOperation op)
    {
        var source = op.Arguments.FirstOrDefault()?.Value;
        while (source is IConversionOperation conversion) source = conversion.Operand;
        var isDictionary = source?.Type is { } type && type.AllInterfaces.Concat(type is INamedTypeSymbol n ? [n] : [])
            .Any(i => i.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
                or "System.Collections.Generic.IDictionary<TKey, TValue>");
        return isDictionary && op.TargetMethod.Parameters.Skip(1).All(p => p.Type.TypeKind != TypeKind.Delegate
            && !p.Type.OriginalDefinition.ToDisplayString().StartsWith("System.Collections.Generic.IEqualityComparer", StringComparison.Ordinal));
    }

    private static bool SourceIs(IInvocationOperation op, params string[] methods)
    {
        var source = op.Arguments.FirstOrDefault()?.Value;
        while (source is IConversionOperation conversion) source = conversion.Operand;
        return source is IInvocationOperation call && methods.Contains(call.TargetMethod.Name);
    }

    // `prices["apple"]` on any dictionary throws when the key is missing. Writing to one is FP0002/FP0005's business.
    private static (string? Name, string? When, string? Instead) ThrowingIndexer(IPropertyReferenceOperation op)
    {
        if (!op.Property.IsIndexer) return default;
        if (op.Parent is IAssignmentOperation assignment && assignment.Target == op) return default;

        // Regex's GroupCollection returns an unsuccessful Group for a missing name instead of throwing.
        if (op.Property.ContainingType.ToDisplayString() == "System.Text.RegularExpressions.GroupCollection") return default;

        // A key lookup: the indexer takes the dictionary's key type. (An int indexer on a string-keyed collection
        // is a list indexer, which this rule leaves alone.)
        var isKeyLookup = op.Property.ContainingType.AllInterfaces.Concat([op.Property.ContainingType])
            .Where(i => i.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
                or "System.Collections.Generic.IDictionary<TKey, TValue>")
            .Any(i => op.Property.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(op.Property.Parameters[0].Type, i.TypeArguments[0]));
        if (isKeyLookup && op.Instance is { } dictionary && op.Arguments.FirstOrDefault() is { } key
            && IsGuardedByContainsKey(op, dictionary, key.Value, keyPresent: true)) return default;

        return isKeyLookup
            ? (op.Syntax.ToString(), "the key is missing", "TryGetValue or GetValueOrDefault")
            : default;
    }

    // FP0012 Ignored results -------------------------------------------------

    // Types whose methods never change the value they're called on.
    private static readonly string[] PureValueTypes =
    {
        "System.DateTime", "System.DateTimeOffset", "System.TimeSpan", "System.DateOnly", "System.TimeOnly",
    };

    private static void AnalyzeIgnoredResult(OperationAnalysisContext ctx)
    {
        var statement = (IExpressionStatementOperation)ctx.Operation;
        if (statement.Operation is not IInvocationOperation { TargetMethod: { ReturnsVoid: false } method } invocation) return;
        if (!ReturnsNewValue(method)) return;

        Report(ctx, IgnoredResult, invocation.Syntax, $"{method.ContainingType.Name}.{method.Name}");
    }

    private static bool ReturnsNewValue(IMethodSymbol method)
    {
        var type = method.ContainingType;
        var ns = type.ContainingNamespace?.ToDisplayString();

        // ImmutableArray<T>.Builder and friends are mutable, and e.g. Remove returns a bool you may not need.
        // ImmutableInterlocked changes the variable passed by ref, so its result is optional too.
        if (ns == "System.Collections.Immutable") return type.Name is not ("Builder" or "ImmutableInterlocked");
        if (ns == "System.Linq") return type.Name is "Enumerable" or "ImmutableArrayExtensions";
        if (type.SpecialType == SpecialType.System_String) return true;
        if (type.ToDisplayString() == "System.Math") return true;

        // Records (yours or a library's), and readonly structs that are yours or marked [ValueEquality]: their methods
        // return new values. Other libraries' readonly structs can still do things (`CancellationToken.Register`).
        // A method with out or ref parameters hands back results that way, so its return value may be optional.
        var pureStruct = type.IsValueType && type.IsReadOnly
            && (type.Locations.Any(l => l.IsInSource) || type.GetAttributes().Any(a => a.AttributeClass?.Name == "ValueEqualityAttribute"));
        if ((type.IsRecord || pureStruct) && !method.IsStatic
            && method.Parameters.All(p => p.RefKind is RefKind.None or RefKind.In)) return true;

        return PureValueTypes.Contains(type.OriginalDefinition.ToDisplayString());
    }

    // FP0011 Side-effecting APIs ----------------------------------------------

    private static void AnalyzeImpure(OperationAnalysisContext ctx, ImpureApis apis)
    {
        var (member, type) = Referenced(ctx.Operation);
        if (member is null || type is null || !apis.Contains(member, type)) return;

        var isWholeType = member is INamedTypeSymbol || member is IMethodSymbol { MethodKind: MethodKind.Constructor };
        var typeName = type.OriginalDefinition.ToDisplayString();
        bool In(string[] list) => list.Contains(typeName) || list.Contains(typeName + "." + member.Name);
        var why = In(AmbientApis) ? "depends on the machine it runs on"
            : In(NondeterministicApis) ? "runs work in an unpredictable order"
            : "has side effects";
        Report(ctx, ImpureApi, ctx.Operation.Syntax, isWholeType ? type.Name : $"{type.Name}.{member.Name}", why, ImpureRemedy);
    }

    private const string ImpureRemedy = "Call it from an [ImperativeAdapter], or pass the value in.";

    // Diagnostic properties for the code fix: AddNewLineKey names the property to set to "\n" next to the flagged
    // assignment; AddArgumentKey means "pass \"\n\" to the flagged call".
    public const string AddNewLineKey = "AddNewLine";
    public const string AddArgumentKey = "AddNewLineArgument";

    private const string WindowsNewLine = "which is \"\\r\\n\" on Windows";

    // Options whose indented output uses Environment.NewLine unless the newline property is set: "\r\n" on Windows.
    private static readonly (string Type, string Indent, string NewLine)[] IndentedWriters =
    {
        ("System.Text.Json.JsonSerializerOptions", "WriteIndented", "NewLine"),
        ("System.Text.Json.JsonWriterOptions", "Indented", "NewLine"),
        ("System.Xml.XmlWriterSettings", "Indent", "NewLineChars"),
    };

    private static void AnalyzeIndentedWriter(OperationAnalysisContext ctx)
    {
        var creation = (IObjectCreationOperation)ctx.Operation;
        if (creation.Type is not INamedTypeSymbol type || creation.Initializer is null) return;
        var name = type.ToDisplayString();
        var (_, indent, newLine) = IndentedWriters.FirstOrDefault(w => w.Type == name);
        if (indent is null || !type.GetMembers(newLine).Any()) return;

        var assignments = creation.Initializer.Initializers.OfType<ISimpleAssignmentOperation>()
            .Select(a => (Name: (a.Target as IPropertyReferenceOperation)?.Property.Name, Assignment: a))
            .ToList();
        if (assignments.Any(a => a.Name == newLine)) return;

        var indented = assignments.FirstOrDefault(a => a.Name == indent).Assignment;
        if (indented is null || indented.Value.ConstantValue is { HasValue: true, Value: false }) return;

        ctx.ReportDiagnostic(Diagnostic.Create(ImpureApi, indented.Syntax.GetLocation(),
            ImmutableDictionary<string, string?>.Empty.Add(AddNewLineKey, newLine),
            $"{type.Name}.{indent}", "writes Environment.NewLine, " + WindowsNewLine,
            $"Use {newLine} = \"\\n\" in the same initializer."));
    }

    // `text.ReplaceLineEndings()` with no argument uses Environment.NewLine.
    private static void AnalyzeReplaceLineEndings(OperationAnalysisContext ctx)
    {
        var call = (IInvocationOperation)ctx.Operation;
        if (call.TargetMethod is not { Name: "ReplaceLineEndings", Parameters.Length: 0 } method
            || method.ContainingType.SpecialType != SpecialType.System_String) return;

        ctx.ReportDiagnostic(Diagnostic.Create(ImpureApi, call.Syntax.GetLocation(),
            ImmutableDictionary<string, string?>.Empty.Add(AddArgumentKey, "true"),
            "string.ReplaceLineEndings()", "uses Environment.NewLine, " + WindowsNewLine,
            "Use ReplaceLineEndings(\"\\n\") instead."));
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

    // FP0016 Checks switched off ----------------------------------------------

    private const string RemoveIt = "Remove it and fix the code it hides.";

    // Attributes that switch checks off, by full name. Roslyn matches them by symbol, so an alias
    // (using Quiet = ...SuppressMessageAttribute) works as well as the plain name.
    private static readonly string[] SuppressionAttributes =
    {
        "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute",
        "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute",
    };

    // Code marked with this is generated code to Roslyn, so analyzers skip it.
    private const string GeneratedCodeAttribute = "System.CodeDom.Compiler.GeneratedCodeAttribute";

    /// <summary>
    /// What turns checks off inside a file: '#pragma warning disable', [SuppressMessage] and '#nullable disable'.
    /// Public so the gate can run the same scan where a pragma can't hide FP0016 itself. With a semantic model,
    /// attributes are matched by what they bind to, so an alias can't hide them.
    /// </summary>
    public static IEnumerable<Diagnostic> SwitchOffs(SyntaxTree tree, ImmutableArray<string> mayLower, CancellationToken ct) =>
        SwitchOffs(tree, null, mayLower, ct);

    /// <inheritdoc cref="SwitchOffs(SyntaxTree, ImmutableArray{string}, CancellationToken)"/>
    public static IEnumerable<Diagnostic> SwitchOffs(SyntaxTree tree, SemanticModel? model, ImmutableArray<string> mayLower, CancellationToken ct)
    {
        bool Allowed(string id) => mayLower.Contains(id, StringComparer.OrdinalIgnoreCase);
        var root = tree.GetRoot(ct);

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.GetStructure() is PragmaWarningDirectiveTriviaSyntax pragma
                && pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword)
                && (pragma.ErrorCodes.Count == 0 || pragma.ErrorCodes.Any(code => !Allowed(code.ToString().Trim()))))
                yield return Diagnostic.Create(RuleSwitchedOff, pragma.GetLocation(), Short(pragma.ToString()), "turns checks off", RemoveIt);
            else if (trivia.GetStructure() is NullableDirectiveTriviaSyntax nullable
                && nullable.SettingToken.IsKind(SyntaxKind.DisableKeyword) && !Allowed("nullable"))
                yield return Diagnostic.Create(RuleSwitchedOff, nullable.GetLocation(), Short(nullable.ToString()), "turns nullable checks off", RemoveIt);
        }

        var aliases = Aliases(root);
        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            if (AttributeKind(attribute, model, aliases, ct) != SwitchOffAttribute.Suppression) continue;
            // [SuppressMessage("Category", "FP0001:Title")]: the rule ID is the second argument, up to a colon.
            var id = attribute.ArgumentList?.Arguments.Skip(1).FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
                ? literal.Token.ValueText.Split(':')[0].Trim() : "";
            if (!Allowed(id))
                yield return Diagnostic.Create(RuleSwitchedOff, attribute.GetLocation(), Short($"[{attribute}]"), "turns checks off", RemoveIt);
        }
    }

    /// <summary>
    /// Code every analyzer skips, so the build can't report it there: a type or member marked [GeneratedCode], and
    /// lines after '#line hidden'. For the gate, which reports these itself.
    /// </summary>
    public static IEnumerable<Diagnostic> HiddenFromAnalyzers(SyntaxTree tree, SemanticModel? model, CancellationToken ct)
    {
        var root = tree.GetRoot(ct);
        var aliases = Aliases(root);
        var generated = root.DescendantNodes().OfType<AttributeSyntax>()
            .Where(attribute => AttributeKind(attribute, model, aliases, ct) == SwitchOffAttribute.Generated)
            .Select(attribute => Diagnostic.Create(RuleSwitchedOff, attribute.GetLocation(), Short($"[{attribute}]"),
                "makes every analyzer skip what it marks", RemoveIt));
        var hidden = root.DescendantTrivia(descendIntoTrivia: true)
            .Select(trivia => trivia.GetStructure()).OfType<LineDirectiveTriviaSyntax>()
            .Where(line => line.Line.IsKind(SyntaxKind.HiddenKeyword))
            .Select(line => Diagnostic.Create(RuleSwitchedOff, line.GetLocation(), Short(line.ToString()),
                "hides the code after it from every analyzer", RemoveIt));
        return generated.Concat(hidden);
    }

    private enum SwitchOffAttribute { None, Suppression, Generated }

    // Aliases for the attributes, as in 'using Quiet = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;'.
    // Found here so the scan works without a semantic model; global aliases in other files need one.
    private static ImmutableArray<(string Alias, string Target)> Aliases(SyntaxNode root) =>
        root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(u => u.Alias is not null && u.Name is not null)
            .Select(u => (Alias: u.Alias!.Name.Identifier.ValueText, Target: LastName(u.Name!.ToString())))
            .ToImmutableArray();

    private static SwitchOffAttribute AttributeKind(AttributeSyntax attribute, SemanticModel? model,
        ImmutableArray<(string Alias, string Target)> aliases, CancellationToken ct)
    {
        // By symbol: the type the attribute binds to, whatever it's called here.
        var bound = model?.GetSymbolInfo(attribute, ct) is { } info
            ? (info.Symbol ?? info.CandidateSymbols.FirstOrDefault())?.ContainingType?.ToDisplayString()
            : null;
        if (bound is not null && SuppressionAttributes.Contains(bound, StringComparer.Ordinal)) return SwitchOffAttribute.Suppression;
        if (bound == GeneratedCodeAttribute) return SwitchOffAttribute.Generated;

        // By name, and through any alias in this file: '[SuppressMessage]', '[Quiet]'.
        var name = LastName(attribute.Name.ToString());
        // [Quiet] finds an alias called Quiet or QuietAttribute, as the compiler does.
        var target = aliases.Where(a => a.Alias == name || a.Alias == name + "Attribute").Select(a => a.Target).FirstOrDefault() ?? name;
        var simple = target.EndsWith("Attribute", StringComparison.Ordinal) ? target.Substring(0, target.Length - "Attribute".Length) : target;
        return simple switch
        {
            "SuppressMessage" or "UnconditionalSuppressMessage" => SwitchOffAttribute.Suppression,
            // A type of the same name elsewhere doesn't make code generated, so trust the symbol when there is one.
            "GeneratedCode" when bound is null => SwitchOffAttribute.Generated,
            _ => SwitchOffAttribute.None,
        };
    }

    // The last part of a dotted or alias-qualified name: 'System.Diagnostics.CodeAnalysis.SuppressMessage' -> 'SuppressMessage'.
    private static string LastName(string name) =>
        name.Substring(Math.Max(name.LastIndexOf('.'), name.LastIndexOf(':')) + 1).Trim();

    private static string Short(string code)
    {
        var line = code.Trim().Split('\n')[0].Trim();
        return line.Length <= 60 ? line : line.Substring(0, 57) + "...";
    }

    // FP0017 / FP0018 Policy --------------------------------------------------

    // A key that's set (even to "none" or nothing) restricts; an unset key doesn't.
    private static ImmutableArray<string>? PolicyList(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out var value)
            ? SplitList(value).Where(v => !v.Equals("none", StringComparison.OrdinalIgnoreCase)).ToImmutableArray()
            : null;

    private static void AnalyzeAdapterPlacement(SyntaxNodeAnalysisContext ctx)
    {
        var attribute = (AttributeSyntax)ctx.Node;
        if (ctx.SemanticModel.GetSymbolInfo(attribute, ctx.CancellationToken).Symbol?.ContainingType?.Name != AdapterAttributeName) return;
        if (PolicyList(ctx.Options.AnalyzerConfigOptionsProvider.GetOptions(attribute.SyntaxTree), AdapterNamespacesKey) is not { } allowed) return;

        // What it marks: a type, member, local function or lambda. Its namespace is what counts.
        var marked = ctx.SemanticModel.GetDeclaredSymbol(attribute.Parent!.Parent!, ctx.CancellationToken)
            ?? ctx.SemanticModel.GetSymbolInfo(attribute.Parent!.Parent!, ctx.CancellationToken).Symbol
            ?? ctx.ContainingSymbol;
        if (marked is null || IsInNamespaces(marked, allowed)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(AdapterPlacement, attribute.GetLocation(),
            marked.Name.Length > 0 ? marked.Name : "lambda",
            allowed.IsEmpty ? ", and this project allows none" : $" outside '{string.Join(", ", allowed)}'",
            allowed.IsEmpty ? "to a shell project" : "there"));
    }

    private static void AnalyzeReferences(CompilationAnalysisContext ctx)
    {
        if (PolicyList(ctx.Options.AnalyzerConfigOptionsProvider.GlobalOptions, AllowedReferencesKey) is not { } allowed) return;

        var coreFolder = BaseLibraryFolder(ctx.Compilation);
        foreach (var reference in ctx.Compilation.References)
        {
            if (ctx.Compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly) continue;
            if (IsDotNetBaseLibrary(reference, assembly, coreFolder)) continue;
            var name = assembly.Identity.Name;
            if (allowed.Any(a => a.EndsWith("*", StringComparison.Ordinal)
                    ? name.StartsWith(a.Substring(0, a.Length - 1), StringComparison.OrdinalIgnoreCase)
                    : name.Equals(a, StringComparison.OrdinalIgnoreCase))) continue;
            ctx.ReportDiagnostic(Diagnostic.Create(ReferenceOutsidePolicy, Location.None, name));
        }
    }

    // The .NET base library: assemblies from the .NET or .NET Standard reference pack the project compiles against.
    // Three things must hold, so copying a library into a folder with the right name isn't enough:
    // - the folder is named after the pack (the SDK's packs/Microsoft.NETCore.App.Ref/11.0.0, NuGet's
    //   microsoft.netcore.app.ref/11.0.0 or netstandard.library/2.0.3, or an unpacked Microsoft.NETCore.App.Ref.11.0.0);
    // - it's the same folder as the assembly that defines System.Object, so the pack really is the base library;
    // - the assembly is signed with one of the keys .NET's own assemblies use.
    // Not ASP.NET Core's or Windows Desktop's packs (another folder), and not Microsoft's NuGet packages.
    private static readonly string[] BaseLibraryPacks = { "/microsoft.netcore.app.ref", "/netstandard.library" };

    // Public key tokens of the assemblies in the .NET and .NET Standard reference packs (Microsoft, ECMA, .NET Core,
    // the Silverlight-era key some facades keep, and the Microsoft.Extensions key the .NET 11 pack includes).
    private static readonly string[] BaseLibraryKeys =
        { "b03f5f7f11d50a3a", "b77a5c561934e089", "cc7b13ffcd2ddd51", "31bf3856ad364e35", "adb9793829ddae60" };

    private static string? BaseLibraryFolder(Compilation compilation) =>
        compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly is { } core
        && compilation.GetMetadataReference(core) is PortableExecutableReference { FilePath: { } path } && IsInBaseLibraryPack(path)
            ? Folder(path) : null;

    private static bool IsDotNetBaseLibrary(MetadataReference reference, IAssemblySymbol assembly, string? coreFolder)
    {
        if (coreFolder is null || reference is not PortableExecutableReference { FilePath: { } path }) return false;
        var token = string.Concat(assembly.Identity.PublicKeyToken.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        return string.Equals(Folder(path), coreFolder, StringComparison.OrdinalIgnoreCase)
            && BaseLibraryKeys.Contains(token, StringComparer.Ordinal);
    }

    private static bool IsInBaseLibraryPack(string path)
    {
        var normal = path.Replace('\\', '/');
        return BaseLibraryPacks.Any(pack =>
        {
            var at = normal.IndexOf(pack, StringComparison.OrdinalIgnoreCase);
            return at >= 0 && at + pack.Length < normal.Length && normal[at + pack.Length] is '/' or '.';
        });
    }

    private static string Folder(string path)
    {
        var normal = path.Replace('\\', '/');
        return normal.Substring(0, Math.Max(normal.LastIndexOf('/'), 0));
    }

    // Exemptions --------------------------------------------------------------

    // Decides, once per file and once per symbol in each compilation, whether the rules apply.
    private sealed class Guard
    {
        private readonly ConcurrentDictionary<SyntaxTree, ImmutableArray<string>> _legacy = new();
        private readonly ConcurrentDictionary<ISymbol, bool> _exempt = new(SymbolEqualityComparer.Default);

        public Action<OperationAnalysisContext> Op(Action<OperationAnalysisContext> rule) => ctx =>
        {
            if (!Exempt(ctx.ContainingSymbol, ctx.Options, ctx.Operation.Syntax.SyntaxTree) && !InAdapterFunction(ctx.Operation))
                rule(ctx);
        };

        public Action<SymbolAnalysisContext> Symbol(Action<SymbolAnalysisContext> rule) => ctx =>
        {
            var tree = ctx.Symbol.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree;
            if (tree is not null && !Exempt(ctx.Symbol, ctx.Options, tree)) rule(ctx);
        };

        private bool Exempt(ISymbol symbol, AnalyzerOptions options, SyntaxTree tree)
        {
            var legacy = _legacy.GetOrAdd(tree, t => LegacyNamespaces(options, t));
            return _exempt.GetOrAdd(symbol, s => IsExempt(s, legacy));
        }

        // An operation inside a local function or lambda marked [ImperativeAdapter]. (Its ContainingSymbol is
        // the enclosing method, so the symbol check above can't see the attribute.)
        private static bool InAdapterFunction(IOperation operation)
        {
            // Start at the operation itself: a marked local function's own declaration is exempt too (FP0010).
            for (var current = operation; current is not null; current = current.Parent)
            {
                var function = current switch
                {
                    ILocalFunctionOperation local => local.Symbol,
                    IAnonymousFunctionOperation lambda => lambda.Symbol,
                    _ => null,
                };
                if (function is not null && HasAdapterAttribute(function)) return true;
            }
            return false;
        }
    }

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

    // `List<int>`, `int`, and nested types with their container: `ImmutableArray<int>.Builder`, not `Builder`.
    private static readonly SymbolDisplayFormat TypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static string Display(ITypeSymbol type) => type.ToDisplayString(TypeFormat);

    private static void Report(OperationAnalysisContext ctx, DiagnosticDescriptor rule, SyntaxNode node, params object[] args) =>
        ctx.ReportDiagnostic(Diagnostic.Create(rule, node.GetLocation(), args));
}
