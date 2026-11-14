# Working on this repository

For coding agents changing FunctionalLint or the sample apps. If you're writing code in a project that *uses* the linter, read [`FunctionalLint/agents/AGENTS.md`](FunctionalLint/agents/AGENTS.md) instead: it's the guide for that, meant to be copied into other repositories.

## What this is

- **FunctionalLint**: a Roslyn analyzer (rules FP0001–FP0022), its code fixes, and a heal tool. Together they keep a C# project to a strict functional subset.
- **samples/**: four small apps and a shared `Functional` library, written in that subset. They exist to test the linter on real code.
- **specs/**: what each sample app must do.

The SDK version is pinned in `global.json` (.NET 11, C# 15).

## Map

| Path | What's there |
|---|---|
| `FunctionalLint/src/FunctionalLint/` | The analyzer. One `partial class FunctionalAnalyzer` split by theme. |
| `  FunctionalAnalyzer.cs` | Rule descriptors, registration (`Initialize`), most rules, exemptions (`Guard`). |
| `  Subset.cs` | FP0019, the language subset: an allowlist of syntax kinds. |
| `  ApiSubset.cs` | FP0020, the .NET API subset: an allowlist. |
| `  Meaning.cs` | Casts, division, hash order (FP0021), float comparison (FP0022). |
| `  Indexing.cs` | FP0015 for indexing and default `ImmutableArray`. |
| `  Guards.cs` | The one engine for "checked first" (`IsGuarded`), shared by division, key lookups and indexing. |
| `  Adapters.cs` | The analyzer's own side effects and caches (`Memo`, `Effects`, `Sequences`). |
| `FunctionalLint/src/FunctionalLint.CodeFixes/` | Code fixes. |
| `FunctionalLint/tools/FunctionalLint.Heal/` | `heal.sh`'s engine: fix, check (the gate), hooks, check server. Uses Roslyn 5.9. |
| `FunctionalLint/tests/FunctionalLint.Tests/` | `Rules/` (one file per rule), `Fixes/`, `Guidance/` (messages, catalog, packaging). |
| `FunctionalLint/docs/rules/FPxxxx.md` | One doc per rule. |
| `FunctionalLint/build/` | `FunctionalCore.props` and the settings every core imports. |
| `FunctionalLint/agents/` | Hooks, Claude Code settings, the consumer `AGENTS.md`, and the agent eval. |
| `functionallint.policy` | The owner's rules for the gate. Protected: see below. |
| `.github/workflows/` | `ci.yml` (every PR) and `release.yml` (on a `v*` tag). |

## Commands

```
dotnet test FunctionalLint                                  # linter tests (~25 s)
dotnet test samples/Samples.slnx                            # sample apps and library
FunctionalLint/heal.sh samples/Samples.slnx --check         # the gate on the samples
FunctionalLint/heal.sh FunctionalLint/FunctionalLint.slnx --check   # the gate on the linter itself
FunctionalLint/agents/hook-smoke-test.sh samples/Ledger/Ledger.Core/Model.cs   # hooks end to end
FunctionalLint/heal.sh <project.csproj>                     # apply the auto-fixes
```

`heal.sh` rebuilds the heal tool when any source file changed. `FUNCTIONALLINT_TIMING=1` prints where a check spends its time. `FunctionalLint/tools/scale-test.sh` measures performance on a large generated core (see `FunctionalLint/docs/performance.md`).

## Done means

All four pass, as in CI:
1. `dotnet test FunctionalLint`;
2. `dotnet test samples/Samples.slnx`;
3. the gate on both solutions, with no findings;
4. the hook smoke test, if you touched the heal tool or hooks.

CI runs them on Linux, macOS and Windows.

## The linter follows its own rules

The analyzer and code fixes are functional cores (listed as `gate_only` in the policy), so the gate checks code in `src/` like any core:
- **Language:** no loops, no reassignment, no `throw`, no `object`.
- **Collections:** no mutable collections; use list patterns rather than `xs[0]`.
- **Switches:** every switch over an enum lists every case. For Roslyn's large enums, use a map rather than a switch with `_`.
- **Side effects** go in `FunctionalLint.Adapters` (`Adapters.cs`).
- **Target is netstandard2.0.** `Index.cs` and `IsExternalInit.cs` are polyfills for list patterns and records.
- **Roslyn 4.12.** The analyzer is built against it, so syntax newer than that (C# 14–15) is matched by kind name, as in `Subset.cs`'s `AllowedByName`.

The heal tool and the tests aren't cores: ordinary C# is fine there.

## Adding or changing a rule

`RuleCatalogTests` and `MessageTests` catch most of these if you forget one:
1. **Descriptor and registration.** Add a `DiagnosticDescriptor` (next free ID, made with `Rule(...)`). Add it to `SupportedDiagnostics`, and register its action in `Initialize`, wrapped in `GuardOp(...)` so adapters and legacy code are skipped.
2. **Rule tests.** Add `tests/.../Rules/FPxxxx_Name.cs`, with `{|FPxxxx:code|}` markup for what's reported and a test of what isn't. FP0019 and FP0020 are only checked in tests that mark them.
3. **Message test.** Add a row in `Guidance/MessageTests.cs` with the exact message.
   - It must be one line, at most 110 characters once quoted names are removed.
   - It states the problem first, then the remedy.
   - The remedy starts with one of the listed verbs (`Use`, `Mark`, `Move`, `Return`...).
4. **Rule doc.** Add `docs/rules/FPxxxx.md` with `## Why`, `## Instead` (before and after) and `## Auto-fix`.
5. **Listings.**
   - Add a row to the rule table in `FunctionalLint/README.md`.
   - Add the ID to `WarningsNotAsErrors` in `build/FunctionalCore.Settings.props`.
   - If it changes how code should be written, update `FunctionalLint/agents/AGENTS.md`.
6. **Run the gate on both solutions.** A new rule usually finds something in the samples or in the linter. Fix the code, not the rule.

For anything that should accept "already checked" code, give `IsGuarded` a `Check` in `Guards.cs`. Don't write another walker.

## Don't

- **Don't edit `functionallint.policy`, the `.globalconfig` files or `FunctionalCore*.props` to get the gate green.** CI checks pull requests against `main`'s policy anyway. Change them only when the task is to change the rules, and say so.
- **Don't silence a finding.** That means no `#pragma`, no `[SuppressMessage]`, no lowered severity, and no `[ImperativeAdapter]` just to get past a rule. If a rule is wrong, fix the rule and add a test that shows it.
- **Don't skip or delete tests to get green.**
- **Don't run the test suite with `FUNCTIONALLINT_NO_SERVER=1` exported.** It turns the check server off, so the server tests fail.

## Where else to look

- [`FunctionalLint/README.md`](FunctionalLint/README.md): the full human guide (setup, policy, adapters, hooks, limits).
- [`samples/README.md`](samples/README.md): how the sample apps are built. Each app's `FINDINGS.md` says where the linter helped or got in the way.
- [`FunctionalLint/docs/performance.md`](FunctionalLint/docs/performance.md): timings and what was tuned.
