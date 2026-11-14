using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FunctionalLint.Adapters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FunctionalLint;

// One way to tell whether code has already checked something before an operation that would otherwise throw: a key
// lookup (FP0015), a division by what may be 0, an index that may be out of range. It reads what the code says on the
// way up: `?:` and `if` branches, `&&` and `||`, switch arms (their pattern and `when`), `is` patterns, and early
// returns earlier in a block (`if (words.Length < 2) return ...;`). Each rule says which facts count.
public sealed partial class FunctionalAnalyzer
{
    /// <summary>What a rule accepts as a check.</summary>
    /// <param name="Condition">True if this condition, being `whenTrue`, rules out the problem (a leaf: no !, && or ||).</param>
    /// <param name="Pattern">True if a value named by any of these texts matching this pattern rules out the problem.</param>
    private sealed record Check(Func<IOperation, bool, bool> Condition, Func<IPatternOperation, ImmutableArray<string>, bool> Pattern);

    private static bool IsGuarded(IOperation at, Check check)
    {
        bool Holds(IOperation condition, bool whenTrue) => Unwrapped(condition) switch
        {
            IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } not => Holds(not.Operand, !whenTrue),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } both when whenTrue =>
                Holds(both.LeftOperand, true) || Holds(both.RightOperand, true),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } either when !whenTrue =>
                Holds(either.LeftOperand, false) || Holds(either.RightOperand, false),
            IIsPatternOperation { Pattern: INegatedPatternOperation negated } match when !whenTrue =>
                check.Pattern(negated.Pattern, Names(match.Value)),
            IIsPatternOperation match when whenTrue => check.Pattern(match.Pattern, Names(match.Value)),
            { } leaf => check.Condition(leaf, whenTrue),
            null => false,
        };

        var chain = Sequences.Unfold(at, o => o.Parent);
        return chain.Zip(chain.Skip(1), (child, parent) => parent switch
        {
            IConditionalOperation c when c.WhenTrue == child => Holds(c.Condition, whenTrue: true),
            IConditionalOperation c when c.WhenFalse == child => Holds(c.Condition, whenTrue: false),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } b when b.RightOperand == child => Holds(b.LeftOperand, true),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr } b when b.RightOperand == child => Holds(b.LeftOperand, false),
            ISwitchExpressionArmOperation arm when arm.Value == child || arm.Guard == child =>
                arm.Parent is ISwitchExpressionOperation switchExpression && check.Pattern(arm.Pattern, Names(switchExpression.Value))
                || arm.Value == child && arm.Guard is { } guard && Holds(guard, whenTrue: true),
            IBlockOperation block => EarlierExits(block, child).Any(condition => Holds(condition, whenTrue: false)),
            _ => false,
        }).Any(guarded => guarded);
    }

    // `if (condition) return ...;` (or break, continue) before the child in a block: after it, the condition is false.
    private static IEnumerable<IOperation> EarlierExits(IBlockOperation block, IOperation child) =>
        block.Operations.TakeWhile(o => o != child)
            .OfType<IConditionalOperation>()
            .Where(c => c.WhenFalse is null && AlwaysLeaves(c.WhenTrue))
            .Select(c => c.Condition);

    private static bool AlwaysLeaves(IOperation operation) => operation switch
    {
        IReturnOperation or IBranchOperation or IThrowOperation => true,
        IBlockOperation block => block.Operations.LastOrDefault() is { } last && AlwaysLeaves(last),
        _ => false,
    };

    // The texts that name a matched value: `words` for `words is [...]`, `words.Length` for `words.Length switch`.
    private static ImmutableArray<string> Names(IOperation? value) =>
        Unwrapped(value) is { } v ? [v.Syntax.ToString()] : [];

    // Facts about lengths in a pattern: the paths (from the names) that it shows have at least some number of items.
    // `[_, _, ..]` on words: words has at least 2. `{ Parameters.Length: 1 } method` on op: op.Parameters and
    // method.Parameters have at least 1. `3` on words.Length: words has at least 3.
    private static IEnumerable<(string Path, int AtLeast)> LengthFacts(IPatternOperation pattern, ImmutableArray<string> names)
    {
        var counted = names.SelectMany(n => new[] { ".Length", ".Count" }.Where(n.EndsWith).Select(suffix => n.Substring(0, n.Length - suffix.Length)));
        var direct = pattern switch
        {
            IListPatternOperation list => names.Select(n => (n, list.Patterns.Count(p => p is not ISlicePatternOperation))),
            IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: int k } } => counted.Select(path => (path, k)),
            IRelationalPatternOperation { OperatorKind: BinaryOperatorKind.GreaterThan, Value.ConstantValue: { HasValue: true, Value: int k } } =>
                counted.Select(path => (path, k + 1)),
            IRelationalPatternOperation { OperatorKind: BinaryOperatorKind.GreaterThanOrEqual, Value.ConstantValue: { HasValue: true, Value: int k } } =>
                counted.Select(path => (path, k)),
            _ => [],
        };
        var nested = pattern switch
        {
            IRecursivePatternOperation recursive => recursive.PropertySubpatterns.SelectMany(sub =>
            {
                // The matched value is known by its own names and by the variable the pattern declares.
                var aliases = recursive.DeclaredSymbol is { } declared ? names.Add(declared.Name) : names;
                var member = MemberPath(sub.Member);
                return member is null ? [] : LengthFacts(sub.Pattern, [.. aliases.Select(a => $"{a}.{member}")]);
            }),
            IBinaryPatternOperation { OperatorKind: BinaryOperatorKind.And } both =>
                LengthFacts(both.LeftPattern, names).Concat(LengthFacts(both.RightPattern, names)),
            IBinaryPatternOperation { OperatorKind: BinaryOperatorKind.Or } either =>
                // Both sides must say it: take the smaller count each path has on both.
                LengthFacts(either.LeftPattern, names).Join(LengthFacts(either.RightPattern, names),
                    l => l.Path, r => r.Path, (l, r) => (l.Path, Math.Min(l.AtLeast, r.AtLeast))),
            _ => [],
        };
        return direct.Concat(nested);
    }

    // `Parameters.Length` for an extended property pattern, `Parameters` for a plain one.
    private static string? MemberPath(IOperation member) => member switch
    {
        IPropertyReferenceOperation { Instance: IInstanceReferenceOperation } p => p.Property.Name,
        IFieldReferenceOperation { Instance: IInstanceReferenceOperation } f => f.Field.Name,
        IPropertyReferenceOperation { Instance: { } inner } p => MemberPath(inner) is { } prefix ? $"{prefix}.{p.Property.Name}" : null,
        IFieldReferenceOperation { Instance: { } inner } f => MemberPath(inner) is { } prefix ? $"{prefix}.{f.Field.Name}" : null,
        _ => null,
    };
}
