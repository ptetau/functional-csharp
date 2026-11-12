# FunctionalLint

A Roslyn analyzer that keeps a C# project functional. It reports errors at build time and shows squiggles in the IDE.

## Rules

| Rule   | Forbids | Do this instead | Auto-fix |
|--------|---------|-----------------|----------|
| [FP0001](docs/rules/FP0001.md) | `for`, `foreach`, `while`, `do`, `goto`, `.ForEach(...)` | LINQ, `Aggregate`, recursion | |
| [FP0002](docs/rules/FP0002.md) | Reassigning variables, `+=`, `++`, setting fields, array writes, event `+=`, `out`/`ref` to existing variables, `Array.Fill`, `CopyTo` into an existing array and friends | New values, `with` expressions | |
| [FP0003](docs/rules/FP0003.md) | Non-readonly fields, `set` accessors, non-readonly record structs | `readonly`, `init`, `readonly record struct` | When safe |
| [FP0004](docs/rules/FP0004.md) | Plain classes | Records for data, static classes for functions | When safe |
| [FP0005](docs/rules/FP0005.md) | Creating `List<T>`, `Dictionary<,>`, `StringBuilder`, builders, streams and other mutable collections, including `ToList()`; locals typed `IList<T>` etc. | `System.Collections.Immutable` types | Locals |
| [FP0006](docs/rules/FP0006.md) | Calling legacy code directly | Call it through an `[ImperativeAdapter]` | |
| [FP0007](docs/rules/FP0007.md) | `throw`, `ThrowIf...` helpers, `ExceptionDispatchInfo.Throw` | Return an `Error` on your `Result<T>` | |
| [FP0008](docs/rules/FP0008.md) | `IList<T>`, `ICollection<T>`, `IDictionary<,>`, `List<T>` etc. in signatures | `IReadOnlyList<T>`, `ImmutableArray<T>` | Yes |
| [FP0009](docs/rules/FP0009.md) | Adapters with legacy types in their public signatures | Return functional types | |
| [FP0010](docs/rules/FP0010.md) | `async void` | Return `Task` | Yes |
| [FP0011](docs/rules/FP0011.md) | Clock, `Guid.NewGuid`, `Random`, `Console`, `Debug`, file I/O, `HttpClient` and other network APIs, reflection that reads or runs members, current culture, parallel LINQ, hidden `Environment.NewLine` (indented JSON/XML, `ReplaceLineEndings()`) and similar | An adapter, or pass the value in | Line endings |
| [FP0012](docs/rules/FP0012.md) | Ignoring the result of `list.Add(x)`, LINQ, string, date or `Math` methods, or methods on your own records | Use the result: `var updated = list.Add(x)` | |
| [FP0013](docs/rules/FP0013.md) | Catch-all arms (`_`, `var x`, `object`...), switch statements and switching on a cast, over enums, `closed` types and unions | A switch expression that lists every case | |
| [FP0014](docs/rules/FP0014.md) | Collections that compare by reference (`ImmutableArray`, lists, arrays) inside records | `ValueList<T>` or another `[ValueEquality]` type | |
| [FP0015](docs/rules/FP0015.md) | Calls that throw: `First`, `Single`, `Parse`, `dict[key]`, `ToDictionary`... | `FirstOrDefault`, list patterns, `TryParse`, `TryGetValue` | |
| [FP0016](docs/rules/FP0016.md) | `#pragma warning disable`, `[SuppressMessage]`, `#nullable disable`; with a policy, also lowered rules and compiler checks, nullable or analyzers off, code marked generated, a second policy file, a core that stopped loading FunctionalLint | Fix what the rule reports | |
| [FP0017](docs/rules/FP0017.md) | With a policy: `[ImperativeAdapter]` outside the policy's adapter namespaces | Pass the value in, or move the adapter | |
| [FP0018](docs/rules/FP0018.md) | With a policy: references beyond the .NET base library and the policy's list | Move that code to a shell project | |

Each rule's page has a before/after example. The build output links to it.

These are still allowed:
- Object initialisers and `with` expressions, because they build a new value.
- Deconstruction into new variables.
- Constructors and `init` accessors setting their own type's members.
- Attribute classes.
- Calling methods with `out` parameters and assigning your own `out` parameters, so `TryParse` style APIs work.
- Top-level statements (they're host code).

## Requirements

.NET 11 SDK and C# 15 (the default for `net11.0`). The repository's `global.json` pins the SDK.
C# 15 matters here: `closed` types and `union` types let the compiler check that a switch handles every case.

## Choices: closed records and unions

Model "one of these" as a C# 15 `closed` record, and consume it with a switch expression that lists every case:

```csharp
public closed record Payment;
public sealed record Card(string Last4) : Payment;
public sealed record Cash : Payment;

public static string Describe(Payment p) => p switch
{
    Card c => $"card ending {c.Last4}",
    Cash => "cash",
};
```

Add a `Voucher` case later and every switch that doesn't handle it stops compiling (CS8509, an error under the props file). FP0013 stops the ways around that: a catch-all arm (`_`, `var x`, `object`...), a switch statement, and switching on a cast such as `(object)p`. A `union Pet(Cat, Dog)` works the same way when the cases already exist as separate types.

## Set up a core project

Import the shared props file into each functional core project:

```xml
<Import Project="..\FunctionalLint\build\FunctionalCore.props" />
```

The props file:
- Adds the analyzer and its code fixes.
- Turns on nullable reference types and makes every warning an error. Rules you deliberately lower to warnings (FunctionalLint's and the culture rules) stay warnings.
- Turns on the .NET culture rules (CA1304, CA1305, CA1309, CA1310, CA1311) as errors, from `FunctionalCore.globalconfig`. Culture is a hidden input like the clock: `amount.ToString("0.00")` prints `3117,60` on a German machine.
- Makes non-exhaustive switch expressions (CS8509) errors, so a switch over a `closed` type, a `union` or an enum must handle every case.
- Loads `functional.globalconfig`, where you set the legacy namespaces and any extra side-effecting APIs.

Leave the props file out of host, test and adapter-only projects.

### Or use the NuGet package

To share the linter across repositories, build the package:

```
dotnet pack src/FunctionalLint.Package -c Release -o artifacts
```

Push it to your feed, then reference it from each functional core project:

```xml
<PackageReference Include="FunctionalLint" Version="0.3.0" PrivateAssets="all" />
```

The package carries the analyzer, the code fixes and the same compiler settings as the props file. It doesn't include `functional.globalconfig`; put your `functional_lint.*` settings in your own `.globalconfig` or `.editorconfig`.

## Existing imperative code and adapters

Some code is exempt from every rule:
- Code in the namespaces listed in `functional_lint.legacy_namespaces`.
- Anything marked `[ImperativeAdapter]`.

Functional code can only reach legacy code through an adapter (FP0006). So every crossing point is explicit and easy to search for.

Define the attribute once. The analyzer matches it by name.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property)]
public sealed class ImperativeAdapterAttribute : Attribute { }
```

An adapter takes legacy services in its constructor. (The examples use `Result`, `Error` and `Option` from [`samples/Functional`](../samples/Functional), a small library you can copy.) Its public members return only functional types, which FP0009 checks.

```csharp
[ImperativeAdapter]
public sealed class Orders(OrderService service)
{
    public Result<ImmutableArray<Order>> Open()
    {
        try
        {
            return Result.Ok(service.GetOpenOrders().Select(Order.From).ToImmutableArray());
        }
        catch (SqlException ex)
        {
            return new Error("unavailable", ex.Message);
        }
    }
}
```

Use the same pattern for side effects. Wrap `TimeProvider`, `HttpClient` or a `DbContext` in an adapter, or pass values like "now" in as parameters.

## Tune it

Two settings live in `functional.globalconfig`:
- `functional_lint.legacy_namespaces`: comma-separated namespaces of existing imperative code (FP0006, FP0009).
- `functional_lint.impure_apis`: extra side-effecting APIs for FP0011, as a type (`Acme.Mailer`) or one member (`Acme.Mailer.Send`).

While migrating, you can lower a rule in `functional.globalconfig` or `.editorconfig`:

```ini
dotnet_diagnostic.FP0008.severity = warning
```

`#pragma warning disable`, `[SuppressMessage]` and `#nullable disable` are reported in a core (FP0016). Once a repository has a policy, lowered rules are too, unless the policy allows them.

## Policy: the owner's rules

An agent stuck on a rule will reach for whatever switches it off: a pragma, an `[ImperativeAdapter]`, a config setting, or deleting the `FunctionalCore.props` import. A policy file puts those decisions with the repository's owner. Put `functionallint.policy` at the repository root ([this repo's](../functionallint.policy) is an example). Paths in it are relative to the repository root (the git top level), so another policy file further down can't change which projects are cores:

```ini
is_global = true
global_level = 200
functional_lint.cores = src/*.Core/*.Core.csproj      # which projects are functional cores (paths or globs)
functional_lint.may_lower = none                      # rules (or "nullable") a core may lower or suppress
functional_lint.legacy_namespaces = none              # existing imperative code (FP0006)
functional_lint.adapter_namespaces = Shop.Adapters    # where [ImperativeAdapter] may be used (FP0017)
functional_lint.allowed_references = Functional       # what a core may reference besides .NET (FP0018)
functional_lint.impure_apis =                         # extra side-effecting APIs (FP0011)
```

It's a globalconfig, so `FunctionalCore.props` adds it to the build and the IDE shows the same results. The gate (`heal.sh --check`, the agent hook and CI) is what enforces it. The gate reads the policy itself, so its values win over any `.editorconfig`. Then for each core it reports (FP0016):
- `#pragma warning disable`, `[SuppressMessage]` (by any name, including an alias) and `#nullable disable`, even where a pragma hides FP0016 from the build;
- a rule lowered by `<NoWarn>` or config, and the same for CS8509, the nullable warnings and the culture rules; nullable checking turned off; `RunAnalyzers` or `RunAnalyzersDuringBuild` set to false;
- code marked as generated, which every analyzer skips: an `<auto-generated>` header, a `.g.cs` name, `[GeneratedCode]` or `#line hidden`;
- another `functionallint.policy` between a core and the repository root, which the build would use instead;
- a listed core that no longer imports `FunctionalCore.props`. The gate still checks it with every rule.

FP0017 and FP0018 are off without a policy.

### Protect the rules

The policy only helps if the agent can't edit it. There are three layers, from cheapest to strongest:

1. **Claude Code permissions.** [`agents/claude-settings.json`](agents/claude-settings.json) denies edits to project and build files, `.editorconfig`, `*.globalconfig` and the policy. Claude then has to ask you. It's merged with the hook below. Verified in a live session: the edits were refused, and edits to `.cs` files still worked. A shell command could still change a file, which is what the other two layers are for.
2. **CI checks pull requests against main's policy.** The workflow copies `functionallint.policy` from the base branch and points `FUNCTIONALLINT_POLICY` at it. A pull request that loosens the policy is checked against the old one, and gets a warning saying so. If `FUNCTIONALLINT_POLICY` names a file that doesn't exist, heal stops with exit 2 rather than check without it.
3. **For teams: code owners.** Add the policy and build files to `.github/CODEOWNERS`, and require code-owner review in branch protection. (On a repository with one maintainer, this would block your own pull requests.)

## Fix and learn: the agent loop

Some rules have a mechanical fix. `heal.sh` loads the project once, applies those fixes, and prints what it fixed, the habit that avoids each one, and what's left:

```
$ FunctionalLint/heal.sh src/Core/Core.csproj
== Auto-fixed
  FP0003 x3. Next time: Declare fields 'readonly', properties '{ get; init; }', and 'readonly record struct'.
  FP0004 x2. Next time: Write records for data and static classes for functions.
  FP0008 x2. Next time: Take and return IReadOnlyList / IReadOnlyDictionary / ImmutableArray, never List or IList.
  FP0010 x1. Next time: Make async methods return Task, never void.

== Remaining: 4 to fix by hand (5.4s)
Orders.cs(20,9): error FP0001: 'foreach' loops are not allowed. Use LINQ, Aggregate or recursion instead.
...
```

- `heal.sh <project or solution> --check` reports without changing anything, and marks each problem it could fix `[auto-fixable]`.
- Exit codes: 0 clean, 1 problems left, 2 couldn't run. So it works as a pre-commit hook or an agent's "am I done?" check.
- Given a solution, it only touches projects that import `FunctionalCore.props` (or the package).
- Given a project, it also loads the nearest solution that lists it, so it can see the projects that depend on it. A fix that would break one of them (say, a setter a host project uses) is left for you, and isn't marked `[auto-fixable]`. Without such a solution, the report says dependents weren't checked.

Auto-fixes only make changes that keep the code's meaning. Where that isn't possible (loops, mutation, throws, side effects) the message says what to write instead. A fix is also skipped where it would change meaning in that one place: a class whose instances are compared anywhere in the solution doesn't become a record (records compare by value), and a field of a mutable struct type doesn't become `readonly` (calls on it would run on a copy). Those are left for you and aren't marked `[auto-fixable]`. If a fix makes other code stop compiling, that code was mutating something it shouldn't, and the compiler now points at it.

To fix things one at a time in the IDE, use the light bulb on the error.

## Agents

Two pieces help a coding agent work in the functional subset.

**Tell it the rules up front.** Copy [`agents/AGENTS.md`](agents/AGENTS.md) into your repository's `AGENTS.md` or `CLAUDE.md`. It's a one-screen summary of how to write functional core code and what to do when the lint complains.

**Check every edit.** [`agents/claude-code-hook.sh`](agents/claude-code-hook.sh) is a Claude Code hook. After Claude edits a `.cs` file in a functional core project, it checks that project and hands any problems straight back to Claude. Problems in the edited file come first, with `[auto-fixable]` marks. Files in other projects are ignored. The hook fails an edit (exit 2) for any problem in the edited file, or an error elsewhere in the project. Warnings elsewhere, such as a rule lowered to `warning` while migrating, are listed but don't fail the edit. Add it to `.claude/settings.json`. [`agents/claude-settings.json`](agents/claude-settings.json) has the hook and the permission rules from "Protect the rules":

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Edit|Write|MultiEdit",
        "hooks": [
          { "type": "command", "command": "\"$CLAUDE_PROJECT_DIR\"/FunctionalLint/agents/claude-code-hook.sh", "timeout": 120 }
        ]
      }
    ]
  }
}
```

**Speed.** The first check loads the project, which takes about 4 seconds. It also starts a check server in the background, which keeps the project loaded and re-reads only the files that changed. After that a check takes about half a second. Adding or removing a file, or changing a project file, makes the server reload that project (about 2 seconds). The server stops after 30 idle minutes, or when FunctionalLint is rebuilt.

- `heal.sh --serve` starts the server yourself; `heal.sh <project> --check` uses it whenever it's running. A server only answers checks run with the same `FUNCTIONALLINT_POLICY` it was started with.
- It also reloads when an `.editorconfig`, `.globalconfig` or `Directory.Build.*` file changes, and stops cleanly on SIGTERM.
- Its socket is in a folder only you can open (`$XDG_RUNTIME_DIR/functionallint` or `~/.cache/functionallint`).
- `FUNCTIONALLINT_NO_SERVER=1` turns it off (for example on CI).
- `heal.sh --server-status` says whether it's running.
- [`agents/hook-smoke-test.sh`](agents/hook-smoke-test.sh) `<a .cs file in a clean core project>` runs the hook and the server end to end: a clean edit, an edit with a loop (rejected), then its rewrite. CI runs it on Linux, macOS and Windows.
- The first run of all also builds the heal tool, which takes longer.

**Measure it.** [`agents/eval`](agents/eval/README.md) runs Claude Code headless on fixed tasks in a fresh core project with this setup, and scores the runs. Some tasks invite a bypass on purpose. In the baseline, none of 8 runs tried to switch a check off, and all 8 ended with a clean gate and build.

Other agents can use the same check: `dotnet FunctionalLint/tools/FunctionalLint.Heal/bin/Release/net11.0/FunctionalLint.Heal.dll --hook` reads `{"tool_input": {"file_path": "..."}}` on stdin and exits 2 with the problems on stderr.

## Run the tests

```
dotnet test
```

CI (`.github/workflows/ci.yml`) runs these tests and the sample apps' tests on Linux, macOS and Windows for every pull request and every push to `main`. It also runs `heal.sh --check` on a sample project, runs the agent hook and check server end to end, and packs the NuGet package.

The tests are laid out so it's clear where a new case goes:

| Folder | Checks |
|--------|--------|
| `Rules/` | One file per rule: what it reports, exactly where, and what it allows. |
| `Fixes/` | Each code fix: before and after, including the cases it deliberately leaves alone. Also the heal engine and the agent hook. |
| `Guidance/MessageTests.cs` | The exact text of every message, and that each one names the problem and the remedy on one short line. |
| `Guidance/RuleCatalogTests.cs` | Rules are well formed, and the code, README, rule docs and tests agree. |
| `Guidance/RobustnessTests.cs` | Half-written code never crashes the analyzer, and a large file is analysed quickly. |

In test sources, `{|FP0001:text|}` means "expect FP0001 on exactly this text". Any diagnostic that isn't marked fails the test.

## What it can't catch

- **Mutating methods on library types.** `Array.Sort`, `Array.Fill`, `CopyTo` into an existing array and similar are caught (FP0002), but a method that changes a mutable object from a library isn't. FP0005 and FP0008 keep most mutable collections out in the first place.
- **Mutable objects in readonly fields**, such as a `static readonly JsonSerializerOptions`. `readonly` protects only the reference.
- **Every way to throw.** FP0015 covers the common calls; indexers on arrays and lists, overflow and casts aren't covered. Random-input tests find the rest: see [FP0015](docs/rules/FP0015.md#what-it-doesnt-cover).
- **Mutation inside libraries you call.**
- **Arrays in signatures.** Arrays are allowed, but writing to an element is flagged.

## A warning about recursion

C# doesn't guarantee tail calls, so deep recursion can overflow the stack. Use LINQ (`Select`, `Where`, `Aggregate`) for loop-like work. Keep recursion for shallow structures such as trees.
