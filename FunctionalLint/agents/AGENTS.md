<!--
  Copy this into your repository's AGENTS.md or CLAUDE.md, and fix the paths.
  It tells a coding agent how the functional core works and how to use FunctionalLint's feedback.
-->

## Functional core

Projects that import `FunctionalLint/build/FunctionalCore.props` are written in a strict functional subset of C#.
The build fails on anything outside it. Rules and before/after examples: `FunctionalLint/docs/rules/`.

Write it this way from the start:
- Data is a `record` (or `readonly record struct`) with `init` properties. Functions live in `static class`es.
- No loops: `Select`, `Where`, `Sum`, `Aggregate`, recursion for trees.
- No reassignment: give each new value a new name, or use `with`.
- Collections are `ImmutableArray<T>` / `ImmutableDictionary<,>`; signatures take `IReadOnlyList<T>` and friends.
  `list.Add(x)` returns a new list: always use the result.
- Inside a record, use `ValueList<T>`, not `ImmutableArray<T>` or a list: those compare by reference, so the
  record silently stops being equal to a copy of itself.
- Build collections in one go (`Select(...).ToImmutableArray()`), not by `Aggregate` + `Add`, which copies the
  array every step (O(n²)). For running totals use `Scan`; for a fold that grows a list, use `ImmutableList`.
- Use the forms that can't throw: `FirstOrDefault` or list patterns (`xs is [var first, ..]`), `TryParse`,
  `TryGetValue`, `DefaultIfEmpty(0).Max()`, `GroupBy` before `ToDictionary`. Turn "missing" into an `Option`.
- Say which culture you mean: `ToString("0.00", CultureInfo.InvariantCulture)`, `StringComparison.Ordinal`.
- To report every problem (forms, file parsers), collect them with `Validation<TError, T>`, not `Result`.
- "One of these" is a C# 15 `closed record` (or a `union`). Consume it with a switch expression that lists
  every case, never `_`, so adding a case is a compile error until every switch handles it.
- No `throw`: return a `Result`. No clock, random, I/O, console or current culture: take the value as a parameter.
- Anything imperative (legacy code, I/O, exceptions) goes in a type marked `[ImperativeAdapter]`.

When the lint reports problems:
1. Run `FunctionalLint/heal.sh <project>`. It fixes everything marked `[auto-fixable]` and says what to do differently next time.
2. Fix what's left by hand. Each message says what to write instead.
3. Don't silence a rule with `#pragma` or move code into an adapter just to get past it.
   If a rule seems wrong for a case, stop and ask.
