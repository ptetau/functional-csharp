using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FunctionalLint.Adapters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FunctionalLint;

// Rules about what code means, which syntax alone can't tell: operators that throw (FP0015, with the calls that
// throw), the order of hash-based collections (FP0021), and exact floating-point comparison (FP0022).
public sealed partial class FunctionalAnalyzer
{
    public static readonly DiagnosticDescriptor HashOrder = Rule("FP0021",
        "Hash-based collections have no order",
        "'{0}' takes the order of a hash set or dictionary, which changes between runs. Sort it first with '{1}'.",
        "A hash set or dictionary keeps its items in hash order, which isn't defined: strings are hashed with a seed " +
        "that changes every run. Joining one into a string, copying it to a list, or taking its First makes the output " +
        "change between runs. Sort it first (OrderBy), or keep it in an ImmutableSortedSet or ImmutableSortedDictionary.");

    public static readonly DiagnosticDescriptor ExactFloatComparison = Rule("FP0022",
        "Floating-point numbers aren't compared exactly",
        "'{0}' compares floating-point numbers exactly. {1}.",
        "Rounding makes floating-point results that should be equal differ slightly: 0.1 + 0.2 != 0.3. Comparing two " +
        "computed values with == or != then depends on the order of the arithmetic. Compare within a tolerance " +
        "instead, or use decimal for amounts that must be exact. Comparing with a constant (x == 0, x != 1) is " +
        "allowed: it tests for a value that was set, not computed. NaN never equals anything, so use double.IsNaN.");

    // FP0022: exact floating-point comparison ---------------------------------

    private static void AnalyzeFloatComparison(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } op) return;
        if (!IsFloatingPoint(op.LeftOperand.Type) || !IsFloatingPoint(op.RightOperand.Type)) return;

        var constants = new[] { op.LeftOperand, op.RightOperand }.Select(o => o.ConstantValue).Where(c => c.HasValue).ToImmutableArray();
        var advice = constants.Any(c => c.Value is double.NaN or float.NaN)
            ? "Use double.IsNaN: NaN never equals anything"
            : constants.IsEmpty ? "Use a tolerance: Math.Abs(a - b) < 1e-9" : null;
        if (advice is null) return;
        Report(ctx, ExactFloatComparison, op.Syntax, Short(op.Syntax.ToString()), advice);
    }

    private static bool IsFloatingPoint(ITypeSymbol? type) =>
        type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double || type?.ToDisplayString() == "System.Half";

    // FP0015: casts that throw ------------------------------------------------

    // `(Circle)shape` and `(int)boxed` throw when the value is something else, and `(int)maybe` when it's null.
    // Numeric and enum casts don't throw (outside `checked`, which FP0019 leaves out), and `as` and patterns can't.
    private static void AnalyzeThrowingCast(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IConversionOperation op) return;
        if (op.IsImplicit || op.IsTryCast || op.Type is not { } target) return;
        var conversion = op.GetConversion();
        if (!conversion.IsExplicit || conversion.IsUserDefined) return;

        var (when, instead) =
            conversion.IsNullable && op.Operand.Type is { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                ? ("the value is null", $"?? or a pattern such as 'is {Display(target)} value'")
            : conversion.IsReference || conversion.IsUnboxing
                ? ($"the value isn't a '{Display(target)}'", $"a pattern such as 'is {Display(target)} value'")
            : (null, null);
        if ((when, instead) is not ({ } throwsWhen, { } alternative)) return;
        Report(ctx, ThrowingCall, op.Syntax, Short(op.Syntax.ToString()), throwsWhen, alternative);
    }

    // FP0015: division that throws --------------------------------------------

    // `a / b` and `a % b` throw on integers and decimals when b is 0 (doubles give infinity or NaN instead). A
    // non-zero constant can't be 0, and neither can a value checked first: `b != 0 ? a / b : 0`, `b > 0 && a / b > 1`.
    private static void AnalyzeDivision(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IBinaryOperation op) return;
        if (op.OperatorKind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder)) return;
        if (op.OperatorMethod is not null && op.OperatorMethod.ContainingType.SpecialType != SpecialType.System_Decimal) return;
        if (!ThrowsOnZero(op.RightOperand.Type)) return;

        var divisor = Unwrapped(op.RightOperand);
        if (divisor is null || CannotBeZero(divisor) || IsGuardedAgainstZero(op, divisor)) return;

        var text = Short(divisor.Syntax.ToString());
        Report(ctx, ThrowingCall, op.Syntax, Short(op.Syntax.ToString()), $"'{text}' is 0",
            $"'{text} != 0 ? ... : fallback'");
    }

    // A non-zero constant, Math.Max or Math.Clamp with a positive floor (`Math.Max(1, n)`), or a local set to one.
    private static bool CannotBeZero(IOperation? op, int depth = 0) => depth < 6 && Unwrapped(op) switch
    {
        { ConstantValue.HasValue: true } constant => !IsZero(constant),
        IInvocationOperation { TargetMethod: { Name: "Max", ContainingType.Name: "Math" } } max =>
            max.Arguments.Any(a => IsPositiveConstant(a.Value)),
        IInvocationOperation { TargetMethod: { Name: "Clamp", ContainingType.Name: "Math" } } clamp =>
            clamp.Arguments.FirstOrDefault(a => a.Parameter?.Name == "min") is { } min && IsPositiveConstant(min.Value),
        ILocalReferenceOperation local => CannotBeZero(LocalInitializer(local), depth + 1),
        _ => false,
    };

    private static bool IsPositiveConstant(IOperation op) =>
        Unwrapped(op) is { ConstantValue: { HasValue: true, Value: IConvertible value } }
        && value.ToDouble(System.Globalization.CultureInfo.InvariantCulture) > 0;

    // What a local was set to, when it's set once where it's declared (a core can't set it again: FP0002).
    private static IOperation? LocalInitializer(ILocalReferenceOperation local) =>
        local.Local.DeclaringSyntaxReferences.Length == 1
        && local.Local.DeclaringSyntaxReferences[0].GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax { Initializer.Value: { } value }
        && local.SemanticModel is { } model && value.SyntaxTree == model.SyntaxTree
            ? model.GetOperation(value)
            : null;

    private static bool ThrowsOnZero(ITypeSymbol? type) => type?.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_Decimal or SpecialType.System_Char;

    // A constant that is 0, whatever its numeric type.
    private static bool IsZero(IOperation constant) => constant.ConstantValue.Value switch
    {
        decimal d => d == 0m,
        double d => d == 0d,
        float f => f == 0f,
        IConvertible c => c.ToInt64(System.Globalization.CultureInfo.InvariantCulture) == 0,
        _ => false,
    };

    // Is there a check, on the way up from the division, that the divisor isn't 0 on this side?
    private static bool IsGuardedAgainstZero(IOperation op, IOperation divisor)
    {
        var text = divisor.Syntax.ToString();
        bool IsDivisor(IOperation o) => Unwrapped(o)?.Syntax.ToString() == text;
        bool IsZeroConstant(IOperation o) => Unwrapped(o) is { ConstantValue.HasValue: true } constant && IsZero(constant);

        bool Condition(IOperation condition, bool whenTrue) => condition is IBinaryOperation b
            && (IsDivisor(b.LeftOperand) && IsZeroConstant(b.RightOperand) || IsDivisor(b.RightOperand) && IsZeroConstant(b.LeftOperand))
            && (b.OperatorKind == BinaryOperatorKind.NotEquals && whenTrue
                || b.OperatorKind == BinaryOperatorKind.Equals && !whenTrue
                // `b > 0` (or `0 < b`) rules out 0 when true.
                || b.OperatorKind == BinaryOperatorKind.GreaterThan && IsDivisor(b.LeftOperand) && whenTrue
                || b.OperatorKind == BinaryOperatorKind.LessThan && IsDivisor(b.RightOperand) && whenTrue);

        // `b is not 0`, `b is > 0` (a negated pattern arrives here when the `is` is true).
        bool Pattern(IPatternOperation pattern, ImmutableArray<string> names) => names.Contains(text) && pattern switch
        {
            INegatedPatternOperation { Pattern: IConstantPatternOperation c } => IsZeroConstant(c.Value),
            IRelationalPatternOperation { OperatorKind: BinaryOperatorKind.GreaterThan } r => IsZeroConstant(r.Value),
            _ => false,
        };

        return IsGuarded(op, new Check(Condition, Pattern));
    }

    // FP0021: hash order -------------------------------------------------------

    private static readonly ImmutableHashSet<string> HashOrdered = ImmutableHashSet.Create(StringComparer.Ordinal,
        "System.Collections.Immutable.ImmutableHashSet<T>", "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>",
        "System.Collections.Generic.HashSet<T>", "System.Collections.Generic.Dictionary<TKey, TValue>",
        "System.Collections.Frozen.FrozenSet<T>", "System.Collections.Frozen.FrozenDictionary<TKey, TValue>",
        "System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>");

    // Calls whose result shows the order of their source.
    private static readonly ImmutableHashSet<string> ShowsOrder = ImmutableHashSet.Create(StringComparer.Ordinal,
        "First", "FirstOrDefault", "Last", "LastOrDefault", "ElementAt", "ElementAtOrDefault", "Take", "Skip", "TakeLast",
        "SkipLast", "TakeWhile", "SkipWhile", "ToArray", "ToList", "ToImmutableArray", "ToImmutableList", "ToValueList",
        "Zip", "SequenceEqual", "Chunk", "Index");

    // Steps that keep their source's order, so a hash set's order shows through them.
    private static readonly ImmutableHashSet<string> KeepsOrder = ImmutableHashSet.Create(StringComparer.Ordinal,
        "Select", "Where", "SelectMany", "Cast", "OfType", "Distinct", "DistinctBy", "AsEnumerable", "Append", "Prepend",
        "Concat", "GroupBy", "Reverse");

    private static void AnalyzeHashOrder(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IInvocationOperation call) return;
        var method = call.TargetMethod;

        // string.Join(", ", set) and string.Concat(set) show the order of their sequence argument.
        IEnumerable<IOperation?> sources = method.ContainingType.SpecialType == SpecialType.System_String && method.Name is "Join" or "Concat"
            ? call.Arguments.Select(a => (IOperation?)a.Value)
            : ShowsOrder.Contains(method.Name) ? new[] { call.Instance ?? call.Arguments.FirstOrDefault()?.Value } : Array.Empty<IOperation?>();

        if (sources.Select(HashOrderedSource).FirstOrDefault(s => s is not null) is not { } source) return;
        Report(ctx, HashOrder, call.Syntax, Short(call.Syntax.ToString()), SortSuggestion(source));
    }

    // `[.. set]` into a list or array shows the set's order too.
    private static void AnalyzeHashOrderedSpread(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not ICollectionExpressionOperation collection) return;
        if (IsHashOrdered(collection.Type)) return;   // a set from a set: no order to show
        if (collection.Elements.OfType<ISpreadOperation>().Select(s => HashOrderedSource(s.Operand)).FirstOrDefault(s => s is not null)
            is not { } source) return;
        Report(ctx, HashOrder, collection.Syntax, Short(collection.Syntax.ToString()), SortSuggestion(source));
    }

    // The hash-ordered collection a sequence comes from, through order-keeping steps, or null. Sorting on the way
    // (OrderBy, Order) gives it an order, so it stops there.
    private static IOperation? HashOrderedSource(IOperation? source, int depth = 0) => depth >= 12 ? null : Unwrapped(source) switch
    {
        { Type: var type } s when IsHashOrdered(type) => s,
        // set.Keys, map.Values
        IPropertyReferenceOperation { Property.Name: "Keys" or "Values", Instance: { } owner } when IsHashOrdered(owner.Type) => owner,
        IInvocationOperation step when KeepsOrder.Contains(step.TargetMethod.Name) =>
            HashOrderedSource(step.Instance ?? step.Arguments.FirstOrDefault()?.Value, depth + 1),
        _ => null,
    };

    private static bool IsHashOrdered(ITypeSymbol? type) =>
        type is INamedTypeSymbol named && HashOrdered.Contains(named.OriginalDefinition.ToDisplayString());

    private static string SortSuggestion(IOperation source) =>
        source.Type is INamedTypeSymbol { Arity: 2 } ? "OrderBy(pair => pair.Key)" : "Order()";
}
