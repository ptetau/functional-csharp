# functional-csharp

A Roslyn analyzer that holds C# code to a strict functional subset, and the tools that help people and coding agents stay inside it.

A project that opts in gets build errors for anything outside the subset:
- no loops, reassignment or mutable state;
- no classes, `throw` or side effects (the clock, I/O, the current culture);
- no calls that throw, unless the code checks first (`First`, `dict[key]`, `xs[0]`, `a / b`);
- no escape hatches (`try`, `async`, `out`, `!`, `dynamic`, `object`, open inheritance).

Every message says what to write instead. Many problems fix themselves with one command.

```csharp
// error FP0001: 'for' loops are not allowed. Use LINQ, Aggregate or recursion instead.
public static int Sum(int[] xs) { var total = 0; for (var i = 0; i < xs.Length; i++) total += xs[i]; return total; }

// Fine
public static int Sum(ImmutableArray<int> xs) => xs.Sum();
```

## Why

Code that is pure (no hidden inputs or effects), total (nothing throws) and closed (every case listed) is easier to test, to change and to review. It's also easier for a coding agent to get right, because the compiler catches what review would miss.

The rules are an allowlist. Anything new, including future C# features, is outside the subset until someone reviews it.

## What's here

| Folder | What it is |
|---|---|
| [`FunctionalLint/`](FunctionalLint) | The analyzer (rules FP0001–FP0022), code fixes, the `heal.sh` fix-and-check tool, agent hooks, and the [full guide](FunctionalLint/README.md). |
| [`samples/`](samples) | Four apps built in the subset (a ledger, a website, a ray tracer, a video highlight tool) and the small `Functional` library they share (`Result`, `Option`, `Validation`, `ValueList`). |
| [`specs/`](specs) | What each sample app must do. |
| [`functionallint.policy`](functionallint.policy) | This repository's rules for the gate: which projects are cores, and what they may use. |

## Try it

You need the .NET 11 SDK (`global.json` pins the version).

```
dotnet test FunctionalLint                             # the linter's tests
dotnet test samples/Samples.slnx                       # the sample apps
FunctionalLint/heal.sh samples/Samples.slnx --check    # check every functional core
```

To use it in your own project, import `FunctionalLint/build/FunctionalCore.props` into each core project, or reference the `FunctionalLint` NuGet package. See [Set up a core project](FunctionalLint/README.md#set-up-a-core-project).

## With coding agents

- **Hooks.** Claude Code hooks check each edit and check again before the agent finishes. Problems go straight back to the agent ([setup](FunctionalLint/README.md#agents)).
- **Guide for your repo.** [`FunctionalLint/agents/AGENTS.md`](FunctionalLint/agents/AGENTS.md) tells an agent how to write code in the subset. Copy it into your repository.
- **Guide for this repo.** [`AGENTS.md`](AGENTS.md) is for agents working on this repository itself.

## Learn more

- [Rule reference](FunctionalLint/README.md#rules): every rule, with a doc and before/after examples in [`FunctionalLint/docs/rules/`](FunctionalLint/docs/rules).
- [What it can't catch](FunctionalLint/README.md#what-it-cant-catch).
- [Performance](FunctionalLint/docs/performance.md) on large projects.
- [Findings](samples/README.md): what building the sample apps taught about where the linter helps and where it gets in the way.
