# Performance at scale

[`tools/scale-test.sh`](../tools/scale-test.sh) builds one large functional core and times the analyzer and the gate on it. The core is the four sample apps' cores copied many times, each copy in its own namespace, so it's real code that passes every rule. Nothing in the repository changes.

```
FunctionalLint/tools/scale-test.sh 25     # about 61,000 lines in 800 files
FunctionalLint/tools/scale-test.sh 100    # about 246,000 lines in 3,200 files
```

`FUNCTIONALLINT_TIMING=1` makes the gate print where its time goes: loading, compiling with the analyzers, and the policy checks.

## Results

Measured on a 4-core Linux container, with .NET 11.

| | 61,500 lines | 246,000 lines |
|---|---|---|
| Build without the analyzer | 6.4 s | 22.3 s |
| Build with the analyzer | 7.4 s (+1.0 s) | 27.8 s (+5.5 s) |
| Gate (`heal.sh --check`, cold) | 16.4 s | 43.4 s |

- **The build.** FunctionalLint adds about 15–25% to a build. Across all its rules it's one analyzer, and no single rule dominates its time. The largest parts are the language subset (FP0019) and the checks on references and API use, at a few percent of compile time each.
- **The gate.** It grows about linearly with the code. Most of it is compiling, which it has to do: it reports compiler errors, and the analyzers need the bound code.

## What changed after measuring

The first gate run on 61,500 lines took 39 seconds. Three fixes took it to 16:

1. **Bind the code once (−9 s).** The gate asked the compilation for its errors, then ran the analyzers. Roslyn's analyzer runner works on its own copy of the compilation, so every method body was bound twice. The gate now gets compiler and analyzer diagnostics from the runner in one pass.
2. **Runtime settings (−12 s).** A check is a short burst of work on every core. Profile-guided tiering (on by default) spends that time recompiling hot code, and the workstation garbage collector falls behind. The heal tool now turns tiered PGO off and uses the server GC (`FunctionalLint.Heal.csproj`). A small core's check went from 4.9 s to 4.4 s too.
3. **Wrap the policy's options once per file (−1 s).** The gate replaces the project's settings with the policy's. It built a new set of options on each lookup, and analyzers look options up on almost every callback.

## Edits in a large core

The edit hook checks the whole project an edit belongs to, not just the edited file. That's on purpose: an edit that breaks code elsewhere (a renamed method, a changed record) fails straight away. The cost is a full compile for each check: about 10 s warm on 61,500 lines, against half a second for the sample cores.

If a core gets that big, split it into several projects. The hook only checks the edited file's project, and each project compiles on its own.
