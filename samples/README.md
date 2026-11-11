# Sample apps

Four small apps built in the functional subset of C# that FunctionalLint enforces. They exist to find out where the lint helps, where it gets in the way, and where its guidance falls short. The specs they implement are in [`../specs`](../specs).

| App | Spec | Host |
|-----|------|------|
| `Ledger` | [ledger.md](../specs/ledger.md) | Command line |
| `CharacterReviews` | [character-reviews.md](../specs/character-reviews.md) | ASP.NET Core website |
| `RayTracer` | [ray-tracer.md](../specs/ray-tracer.md) | Command line |
| `HighlightReel` | [highlight-reel.md](../specs/highlight-reel.md) | Command line |

## Layout

Each app has the same three projects:

```
samples/<App>/
  <App>.Core/     The functional core: all domain logic, parsing, validation, formatting.
                  Imports FunctionalCore.props, so the lint applies. References ../../Functional.
  <App>.Cli/      The imperative shell (<App>.Web for the website): reads args, files, the clock,
  or .Web/        the network; calls the core; writes output. Not linted. Keep it thin.
  <App>.Tests/    xUnit tests. Not linted. Each spec example is a test, named after its requirement ID.
  <App>.slnx
  FINDINGS.md     Where the lint helped or hurt while building this app.
```

[`Functional/`](Functional) is shared by every core: `Result<T>`, `Option<T>`, `Error` and `[ImperativeAdapter]`.

## The rules for building them

- Logic goes in the core. The shell only does I/O and wiring.
- Don't get past a lint error with `#pragma`, `[ImperativeAdapter]` or by moving logic into the shell. If the functional way is genuinely awkward, write it the functional way anyway and record the friction in `FINDINGS.md`.
- Consume `Result` and `Option` with `Match`, `Map` and `Bind`. A `switch` over their cases can't be exhaustive in C# (see FINDINGS.md).

## Run

```
dotnet test samples/Samples.slnx
dotnet run --project samples/Ledger/Ledger.Cli -- balance --file my.journal
```
