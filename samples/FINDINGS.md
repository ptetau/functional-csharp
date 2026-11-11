# Findings

Problems found by building the sample apps under FunctionalLint: places the lint got in the way, gave poor guidance, missed something, or where the functional style was hard to express in C#.

Each app keeps its own log in `<App>/FINDINGS.md`. This file collects the ones that matter across apps and what was done about them.

| # | Found in | Area | Finding | Status |
|---|----------|------|---------|--------|
| 1 | Functional | Unions / CS8509 | A `switch` over `Result<T>.Ok` / `Result<T>.Failed` is never exhaustive to the C# compiler, so CS8509 (an error under FunctionalCore.props) demands a `_` arm. The only way to fill it is `throw`, which FP0007 forbids. The README's claim that CS8509 checks "closed hierarchies" is wrong: it only helps with enums. Worked around by giving `Result` and `Option` a `Match` method. | Open |
