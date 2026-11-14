using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FunctionalLint.Adapters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FunctionalLint;

// FP0015, the partial operations on lists: an index that may be out of range, and a default ImmutableArray, which
// throws as soon as it's used.
public sealed partial class FunctionalAnalyzer
{
    private const string OutOfRangeInstead = "a list pattern, ElementAtOrDefault, or a check against Length first";

    // `xs[i]` on an array, list, ImmutableArray, string or span throws when i is out of range, unless the code shows
    // it isn't: a check on the way up (`i < xs.Length`, `xs.Count > 2`, `xs is [_, _, ..]`), or an index that
    // ranges over the collection itself (`Enumerable.Range(0, xs.Length).Select(i => xs[i])`, `xs.Select((x, i) => ...)`).
    // Writes are FP0002's; dictionary lookups are the key check above; slices (`xs[1..]`) aren't checked yet.
    private static void AnalyzeIndexing(OperationAnalysisContext ctx)
    {
        var (collection, index) = ctx.Operation switch
        {
            IArrayElementReferenceOperation { Indices: [var at] } element => (element.ArrayReference, at),
            IPropertyReferenceOperation { Property: { IsIndexer: true, Parameters: [var parameter] } property, Instance: { } instance, Arguments: [var argument] }
                when IsListIndex(parameter.Type) && !IsDictionary(property.ContainingType) => (instance, argument.Value),
            // `xs[^1]` on a list with Count and an int indexer: C# turns it into xs[xs.Count - 1].
            IImplicitIndexerReferenceOperation { Instance: var instance, Argument: var argument } => (instance, argument),
            _ => ((IOperation?)null, (IOperation?)null),
        };
        if (collection is null || index is null) return;
        if (ctx.Operation.Parent is IAssignmentOperation assignment && assignment.Target == ctx.Operation) return;
        // An indexer that implements a list's (IReadOnlyList<T>.this[]) forwards to its own list: throwing on a
        // bad index is that contract.
        if (ctx.ContainingSymbol is IMethodSymbol { AssociatedSymbol: IPropertySymbol { IsIndexer: true } implemented }
            && (implemented.IsOverride || implemented.ContainingType.AllInterfaces.SelectMany(i => i.GetMembers().OfType<IPropertySymbol>())
                .Any(m => m.IsIndexer && SymbolEqualityComparer.Default.Equals(implemented.ContainingType.FindImplementationForInterfaceMember(m), implemented))))
            return;
        // Slices (xs[1..]) aren't checked yet.
        if (index.Type?.ToDisplayString() == "System.Range") return;
        if (IsInRange(ctx.Operation, collection, index)) return;

        Report(ctx, ThrowingCall, ctx.Operation.Syntax, Short(ctx.Operation.Syntax.ToString()), "the index is out of range", OutOfRangeInstead);
    }

    private static bool IsListIndex(ITypeSymbol parameter) =>
        parameter.SpecialType == SpecialType.System_Int32 || parameter.ToDisplayString() == "System.Index";

    private static bool IsDictionary(INamedTypeSymbol type) =>
        type.AllInterfaces.Concat([type]).Any(i => i.OriginalDefinition.ToDisplayString()
            is "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>" or "System.Collections.Generic.IDictionary<TKey, TValue>");

    private static bool IsInRange(IOperation access, IOperation collection, IOperation index)
    {
        var list = Unwrapped(collection)?.Syntax.ToString() ?? "";
        var counts = ImmutableArray.Create($"{list}.Length", $"{list}.Count", $"{list}.Count()");
        bool IsCount(IOperation? o) => Unwrapped(o) is { } u && counts.Contains(u.Syntax.ToString());
        // A constant index is checked through the count instead (xs.Length > 2), so `xs.Length == 0` isn't about index 0.
        bool IsIndex(IOperation? o) => Unwrapped(index) is { ConstantValue.HasValue: false } i && Unwrapped(o)?.Syntax.ToString() == i.Syntax.ToString();
        int? Constant(IOperation? o) => Unwrapped(o) is { ConstantValue: { HasValue: true, Value: int k } } ? k : null;

        // The smallest length that makes this index valid, when it's a constant: xs[2] needs 3, xs[^1] needs 1.
        var needs = Unwrapped(index) switch
        {
            { ConstantValue: { HasValue: true, Value: int i } } when i >= 0 => (int?)(i + 1),
            { Syntax: Microsoft.CodeAnalysis.CSharp.Syntax.PrefixUnaryExpressionSyntax { Operand: Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax literal } hat }
                when hat.OperatorToken.Text == "^" && literal.Token.Value is int fromEnd && fromEnd > 0 => fromEnd,
            _ => null,
        };
        if (needs == 1 && AlwaysHasAnItem(collection)) return true;

        // What `count <op> k`, being `whenTrue`, says about the count: at least how many.
        int? AtLeast(BinaryOperatorKind op, bool countOnLeft, int k, bool whenTrue) => (countOnLeft ? op : Mirror(op), whenTrue) switch
        {
            (BinaryOperatorKind.GreaterThan, true) => k + 1,
            (BinaryOperatorKind.GreaterThanOrEqual, true) => k,
            (BinaryOperatorKind.Equals, true) => k,
            (BinaryOperatorKind.NotEquals, true) when k == 0 => 1,
            (BinaryOperatorKind.LessThan, false) => k,
            (BinaryOperatorKind.LessThanOrEqual, false) => k + 1,
            (BinaryOperatorKind.Equals, false) when k == 0 => 1,
            (BinaryOperatorKind.NotEquals, false) => k,
            _ => null,
        };

        bool Condition(IOperation condition, bool whenTrue) => condition switch
        {
            // i < xs.Length (or xs.Length > i) when true; i >= xs.Length (or xs.Length <= i) when false.
            IBinaryOperation b when IsIndex(b.LeftOperand) && IsCount(b.RightOperand) =>
                b.OperatorKind == BinaryOperatorKind.LessThan && whenTrue || b.OperatorKind == BinaryOperatorKind.GreaterThanOrEqual && !whenTrue,
            IBinaryOperation b when IsCount(b.LeftOperand) && IsIndex(b.RightOperand) =>
                b.OperatorKind == BinaryOperatorKind.GreaterThan && whenTrue || b.OperatorKind == BinaryOperatorKind.LessThanOrEqual && !whenTrue,
            // xs.Length > 2, xs.Count == 3, xs.Length < 2 (when false), for a constant index.
            IBinaryOperation b when IsCount(b.LeftOperand) && Constant(b.RightOperand) is { } k =>
                needs is { } n && AtLeast(b.OperatorKind, countOnLeft: true, k, whenTrue) >= n,
            IBinaryOperation b when IsCount(b.RightOperand) && Constant(b.LeftOperand) is { } k =>
                needs is { } n && AtLeast(b.OperatorKind, countOnLeft: false, k, whenTrue) >= n,
            // xs.Any(), and xs.IsEmpty when false.
            IInvocationOperation { TargetMethod.Name: "Any", Arguments.Length: 1 } any =>
                whenTrue && needs == 1 && any.Arguments.FirstOrDefault() is { } source && Unwrapped(source.Value)?.Syntax.ToString() == list,
            IPropertyReferenceOperation { Property.Name: "IsEmpty" or "IsDefaultOrEmpty", Instance: { } owner } =>
                !whenTrue && needs == 1 && Unwrapped(owner)?.Syntax.ToString() == list,
            _ => false,
        };

        bool Pattern(IPatternOperation pattern, ImmutableArray<string> names) =>
            needs is { } n && LengthFacts(pattern, names).Any(f => f.Path == list && f.AtLeast >= n);

        return IsGuarded(access, new Check(Condition, Pattern)) || RangesOverTheCollection(index, list, counts);
    }

    // `k < count` says what `count > k` does. Equals and NotEquals read the same both ways round.
    private static readonly ImmutableDictionary<BinaryOperatorKind, BinaryOperatorKind> Mirrors =
        ImmutableDictionary<BinaryOperatorKind, BinaryOperatorKind>.Empty
            .SetItem(BinaryOperatorKind.LessThan, BinaryOperatorKind.GreaterThan)
            .SetItem(BinaryOperatorKind.LessThanOrEqual, BinaryOperatorKind.GreaterThanOrEqual)
            .SetItem(BinaryOperatorKind.GreaterThan, BinaryOperatorKind.LessThan)
            .SetItem(BinaryOperatorKind.GreaterThanOrEqual, BinaryOperatorKind.LessThanOrEqual);

    private static BinaryOperatorKind Mirror(BinaryOperatorKind op) => Mirrors.GetValueOrDefault(op, op);

    // string.Split always returns at least one item, unless it's told to drop empty ones.
    private static bool AlwaysHasAnItem(IOperation collection) =>
        Unwrapped(collection) is IInvocationOperation { TargetMethod: { Name: "Split", ContainingType.SpecialType: SpecialType.System_String } } split
        && !split.Arguments.Any(a => a.Value.Syntax.ToString().Contains("RemoveEmptyEntries"));

    // The index is a lambda's parameter, and the lambda runs over the collection's own positions:
    // Enumerable.Range(0, xs.Length).Select(i => xs[i]), Range(0, xs.Length * 3).Select(i => xs[i / 3]),
    // or xs.Select((x, i) => xs[i]).
    private static bool RangesOverTheCollection(IOperation index, string list, ImmutableArray<string> counts)
    {
        var (parameter, divisor) = Unwrapped(index) switch
        {
            IParameterReferenceOperation p => (p.Parameter, 1),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Divide, LeftOperand: var left, RightOperand: var right }
                when Unwrapped(left) is IParameterReferenceOperation p && Unwrapped(right) is { ConstantValue: { HasValue: true, Value: int k } } && k > 0
                => (p.Parameter, k),
            _ => (null, 0),
        };
        if (parameter?.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } lambda) return false;

        var call = Sequences.Unfold(index, o => o.Parent).OfType<IAnonymousFunctionOperation>()
            .FirstOrDefault(f => SymbolEqualityComparer.Default.Equals(f.Symbol, lambda))
            ?.Parent?.Parent?.Parent as IInvocationOperation;   // lambda -> delegate creation -> argument -> call
        if (call?.TargetMethod.ContainingNamespace?.ToDisplayString() != "System.Linq") return false;
        var source = Unwrapped(call.Instance ?? call.Arguments.FirstOrDefault()?.Value);

        // xs.Select((x, i) => ...): i is a position in xs.
        if (lambda.Parameters.Length == 2 && SymbolEqualityComparer.Default.Equals(parameter, lambda.Parameters[1]) && divisor == 1)
            return source?.Syntax.ToString() == list;

        // Enumerable.Range(0, xs.Length [* k]).
        if (lambda.Parameters.Length != 1 || source is not IInvocationOperation { TargetMethod: { Name: "Range", ContainingType.Name: "Enumerable" } } range
            || range.Arguments.Length != 2 || Unwrapped(range.Arguments[0].Value) is not { ConstantValue: { HasValue: true, Value: 0 } }) return false;
        var bound = Unwrapped(range.Arguments[1].Value)?.Syntax.ToString() ?? "";
        return divisor == 1
            ? counts.Contains(bound)
            : counts.Any(c => bound == $"{c} * {divisor}" || bound == $"{divisor} * {c}");
    }

    // A default ImmutableArray holds no array: Length, indexing and enumerating all throw. `[]` and Empty are empty.
    private static void AnalyzeDefaultImmutableArray(OperationAnalysisContext ctx)
    {
        var type = ctx.Operation switch
        {
            IDefaultValueOperation { Type: { } t } => t,
            IObjectCreationOperation { Type: { } t, Arguments.Length: 0, Initializer: null } => t,
            _ => null,
        };
        if (type?.OriginalDefinition.ToDisplayString() != "System.Collections.Immutable.ImmutableArray<T>") return;
        Report(ctx, ThrowingCall, ctx.Operation.Syntax, Short(ctx.Operation.Syntax.ToString()),
            "used, because a default ImmutableArray holds no array", $"[] or {Display(type)}.Empty");
    }
}
