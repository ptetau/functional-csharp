# Findings

What building the four sample apps under FunctionalLint showed: where the lint got in the way, gave poor guidance, missed something, or where the functional style was hard to express in C#.

Each app has its own detailed log (`<App>/FINDINGS.md`). This file merges them, ranked by how much each problem cost. "Seen in" counts how many of the four apps hit it independently.

## The short version

- **All four apps work and pass their specs.** 357 tests in all (Ledger 68, CharacterReviews 76, RayTracer 51, HighlightReel 162). The ray tracer renders the sample scene in 0.4 s against a 10 s target.
- **The lint almost never fired on the real code.** Each agent read `AGENTS.md` first and wrote functional code from the start: 0, 1, 2 and 6 errors across about 2,000 lines of core code. The brief does more than the hook.
- **So each agent also wrote a throwaway file of typical imperative code.** The lint caught between a quarter and a half of the problems in those files. What it missed falls into a few clear groups (below).
- **The real bugs were outside the lint's reach.** Calls that throw, culture-dependent formatting, decimal overflow, records that silently lose value equality, and O(n²) folds. All four apps hit at least two of these.
- **Two auto-fixes produced broken or useless code**, and heal counted them as fixed.
- **Every app needed things the shared library lacks**: an error-accumulating validation type, more `Option` operations, and a cheaper way to write unions.

## 1. Auto-fixes that are wrong (fix first: they break trust in heal)

| # | Seen in | Finding | Proposed fix |
|---|---|---|---|
| 1.1 | 3 | **FP0008/FP0005 fix doesn't compile.** `Dictionary<string,int> Cache = new();` becomes `IReadOnlyDictionary<string,int> Cache = new();`, which is CS0144 (can't create an interface). | Skip the fix when the initializer is a target-typed `new()` or a collection expression, or rewrite the initializer too. |
| 1.2 | 3 | **heal counts a diagnostic as "fixed" when it only disappeared because the code stopped compiling.** The summary said "FP0005 x1 auto-fixed" for the line above. | After each rule's fix-all, compare compiler errors. If the fix added any, throw that rule's fix away and report it as not fixable. |
| 1.3 | 2 | **FP0003 fix on a field that's written elsewhere** (`static int calls; ... calls++`) makes it `readonly` and creates CS0198 next to the existing FP0002. **On a mutable struct with no constructor** it produces a struct whose fields can never be set. | Covered by 1.2's "no new compiler errors" check; also skip structs whose fields are only set by object initialisers. |
| 1.4 | 1 | **FP0004 turns a service class (constructor plus private state) into a record.** It compiles, but changes equality, which contradicts the README's "keeps the code's meaning", and it's still OO in disguise. | Only convert classes with no explicit constructor and no private fields. |

## 2. Problems the lint misses

| # | Seen in | Finding | Proposed fix |
|---|---|---|---|
| 2.1 | 4 | **Calls that throw.** FP0007 only sees the `throw` keyword. `First()`, `Single()`, `Last()`, `int.Parse`, `Enum.Parse`, dictionary indexers, `ToImmutableDictionary` with a duplicate key (which FP0005's own docs recommend), and decimal overflow in `Sum` all pass. Every app either shipped one or guarded one by hand. | New rule for common partial calls, pointing at the `OrDefault`/`TryX` form. Fix the FP0005 doc example. |
| 2.2 | 3 | **Culture is a hidden input.** `d.ToString("0.00")`, `decimal.Parse`, `ToLower()` and `string.Compare` read the machine's culture, just as `DateTime.Now` reads the clock. The ledger prints `3117,60` on a German machine. | Turn on the .NET culture analyzers (CA1304, CA1305, CA1307, CA1309, CA1310, CA1311) as errors in `FunctionalCore.Settings.props`. Cheap. |
| 2.3 | 4 | **Mutable collections that aren't on the list.** `ImmutableArray.CreateBuilder()` + `Add`, `Collection<T>`, `ArrayList`, concurrent collections, `MemoryStream`, `Utf8JsonWriter`. Also `ImmutableList.ForEach(...)` with side effects isn't treated as a loop; following FP0005's advice literally (`ToList()` → `ToImmutableList()`) made an imperative JSON writer pass. | Widen FP0005 to "implements a mutable collection interface and isn't immutable", plus builders and streams. Treat `ForEach` as FP0001. Decide and document a policy on local builders. |
| 2.4 | 3 | **Mutation through `out`, `ref` and calls.** `int.TryParse(s, out existing)`, `Interlocked.Increment(ref n)`, `Array.Fill`, `span.Fill`, and `ImmutableCollectionsMarshal.AsImmutableArray` (which wraps a mutable array as "immutable"). | FP0002: allow only `out var` / `out _`; report `ref` to locals and fields. Add `Fill` and the marshal type to the banned APIs. |
| 2.5 | 3 | **FP0012 doesn't know your own types.** `r.Bind(...);`, `o.Map(...);` and `v.Normalised();` as statements pass. These are the results you're most likely to drop in a functional codebase. | Treat non-void methods on records and readonly structs as pure. |
| 2.6 | 1 | **heal hides compiler warnings.** CS8907 ("parameter is unread") was an infinite recursion, and `heal --check` said "Clean". | `TreatWarningsAsErrors` in `FunctionalCore.Settings.props`. |
| 2.7 | 1 | `AsParallel().Aggregate(...)` over doubles is non-deterministic. `Parallel.For` passes. | Add PLINQ and `Parallel` to the side-effecting APIs. |
| 2.8 | 1 | `default(ImmutableArray<T>)` (e.g. from JSON with a missing key) throws on first use. | Document it next to FP0005's advice. |

## 3. Traps the guidance leads you into

| # | Seen in | Finding | Proposed fix |
|---|---|---|---|
| 3.1 | 3 | **Records holding `ImmutableArray<T>` aren't value-equal.** `ImmutableArray` compares by reference, so the "records + ImmutableArray" style the docs recommend quietly breaks record equality. It failed 8, 17 and 8 tests on first run in three apps. | Add a value-equal array type to `Functional` (or recommend `ImmutableList` + `SequenceEqual`), and say so in AGENTS.md and FP0005. A rule could warn on `ImmutableArray` in a record's members. |
| 3.2 | 3 | **`Aggregate` + `ImmutableArray.Add` is O(n²)**, and `Functional.Result.All` uses it. HighlightReel's `list` took 23 s at 80k milestones; a ray tracer pixel buffer built that way took 3.5 s and 26.5 GB. FP0012's "always use the result" pushes people toward it. | Rewrite `Result.All`. Add `Scan` to `Functional`. Recommend `Select(...).ToImmutableArray()` or `ImmutableList` for folds, and say why in AGENTS.md. |
| 3.3 | 1 | **LINQ in a hot path** was about 20× slower than the maths it wrapped and allocated 230 MB per render. Static lambdas with tuple accumulators fixed it, but nothing says how. | A short performance section in AGENTS.md. |

## 4. Unions (the biggest everyday friction)

| # | Seen in | Finding | Proposed fix |
|---|---|---|---|
| 4.1 | 4 | **A `switch` over a closed record hierarchy is never exhaustive to C#,** so CS8509 (an error under the props) demands `_ => throw`, which FP0007 bans. Every union needs a hand-written abstract `Match` plus an override in each case: 25 lines for Ledger's five commands. The README's claim that CS8509 checks "closed hierarchies" is wrong. | A **diagnostic suppressor** that silences CS8509 when a switch covers every nested case of a hierarchy with a private constructor. Then a plain `switch` works and adding a case makes it an error again. |
| 4.2 | 3 | `Result<T>` isn't covariant, so `Result<Command.Balance>` won't convert to `Result<Command>` without a cast in every case. | Document the cast, or add an `Upcast` helper. |

## 5. Gaps in the shared `Functional` library

| # | Seen in | Finding | Proposed fix |
|---|---|---|---|
| 5.1 | 3 | **No error-accumulating type.** Three specs say "report every problem". Three apps wrote their own `Validation<T>`/`Validated<T>` (30–57 lines each). | Add `Validation<T>` with `Apply`/`Traverse`/`Combine`. |
| 5.2 | 3 | `Option` has no `Bind`, LINQ support, or side-effecting `IfSome`/`Iter` for shells. `Result` has no `MapError` for adding context such as a line number. | Add them. |
| 5.3 | 2 | Collection expressions don't infer inside `Match(o => [o], _ => [])`. | `ToImmutableArray()` on `Option`/`Result`. |
| 5.4 | 1 | `Option<T>` is a class, so using it per ray allocates; the ray tracer used `Hit?` instead. | Note it; consider a struct option for hot paths. |

## 6. Noise and wording

| # | Seen in | Finding | Proposed fix |
|---|---|---|---|
| 6.1 | 4 | **One construct, several errors.** A loop gives FP0001 plus an FP0002 for every loop variable, and those FP0002s give the wrong advice ("bind a new name"). A `record struct P(int X, int Y)` gives three FP0003s for one missing `readonly`. A `StringBuilder` gives FP0005 on `new` and again on each `Append`. | Don't report FP0002 inside a loop FP0001 already reports. Report a record struct once. Only report FP0005 at creation for library calls that return the builder itself. |
| 6.2 | 2 | FP0002's remedy is wrong for array element writes. | Separate wording: "build the array with Select(...).ToArray()". |
| 6.3 | 2 | FP0011 says `Environment.NewLine` and `ProcessorCount` "have side effects". They read ambient state. | Wording: "depends on the machine it runs on". |
| 6.4 | 2 | FP0008 on a private field says "lets callers change the collection". | Field-specific wording. |
| 6.5 | 2 | FP0008's message suggests `ImmutableArray<T>` but the fix writes `IReadOnlyList<T>`. | Make the message match the fix, or the fix match the message. |
| 6.6 | 3 | heal's footer ("each message says what to write instead") is wrong for compiler errors, and CS8509 gets no hint about `Match`. | Footer only when FP errors remain; add a CS8509 hint (or 4.1 removes the need). |
| 6.7 | 1 | FP0007's doc and the README use `Ok<T>`, `NotFound`, `Unavailable`, none of which exist in `Functional`. | Use the real types. |

## 7. Speed of the loop

| # | Seen in | Finding | Proposed fix |
|---|---|---|---|
| 7.1 | 4 | `heal.sh` takes 6–8 s wall clock but reports 4–5 s: `dotnet run --project` re-checks the tool's build every call. The hook, which runs the built dll, takes about 5 s. | Run the dll directly (as the hook does) and report wall time. A long-running check server would get it under 1 s. |

## Spec issues

Fixed in the specs:
- **Ledger L-2** said amounts follow "at least two spaces", but the example uses one. Now: any whitespace.
- **Highlight reel H-7** printed list lines without the number that H-10's `remove <n>` needs. Now numbered.

Left as each app chose (listed in the app logs): slug rules for non-ASCII names, the `/titles/new` route clash, tie-breaks for same-day reviews, the camera's handedness, how reflection depth is counted, output column widths, and what counts as "first used" after a removal.
