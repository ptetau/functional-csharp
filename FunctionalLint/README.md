# FunctionalLint

A Roslyn analyzer that keeps a C# project functional. It reports errors at build time and shows squiggles in the IDE.

## Rules

| Rule   | Forbids | Do this instead | Auto-fix |
|--------|---------|-----------------|----------|
| [FP0001](docs/rules/FP0001.md) | `for`, `foreach`, `while`, `do` | LINQ, `Aggregate`, recursion | |
| [FP0002](docs/rules/FP0002.md) | Reassigning variables, `+=`, `++`, setting fields, array writes, event `+=` | New values, `with` expressions | |
| [FP0003](docs/rules/FP0003.md) | Non-readonly fields, `set` accessors, non-readonly record structs | `readonly`, `init`, `readonly record struct` | Yes |
| [FP0004](docs/rules/FP0004.md) | Plain classes | Records for data, static classes for functions | When safe |
| [FP0005](docs/rules/FP0005.md) | Creating `List<T>`, `Dictionary<,>`, `StringBuilder` and similar, including `ToList()`; locals typed `IList<T>` etc. | `System.Collections.Immutable` types | Locals |
| [FP0006](docs/rules/FP0006.md) | Calling legacy code directly | Call it through an `[ImperativeAdapter]` | |
| [FP0007](docs/rules/FP0007.md) | `throw` | Return an `Error` on your `Result<T>` | |
| [FP0008](docs/rules/FP0008.md) | `IList<T>`, `ICollection<T>`, `IDictionary<,>`, `List<T>` etc. in signatures | `IReadOnlyList<T>`, `ImmutableArray<T>` | Yes |
| [FP0009](docs/rules/FP0009.md) | Adapters with legacy types in their public signatures | Return functional types | |
| [FP0010](docs/rules/FP0010.md) | `async void` | Return `Task` | Yes |
| [FP0011](docs/rules/FP0011.md) | Clock, `Guid.NewGuid`, `Random`, `Console`, file I/O, `HttpClient` and similar | An adapter, or pass the value in | |
| [FP0012](docs/rules/FP0012.md) | Ignoring the result of `list.Add(x)`, LINQ, string or date methods | Use the result: `var updated = list.Add(x)` | |

Each rule's page has a before/after example. The build output links to it.

These are still allowed:
- Object initialisers and `with` expressions, because they build a new value.
- Deconstruction into new variables.
- Constructors and `init` accessors setting their own type's members.
- Attribute classes.
- Calling methods with `out` parameters and assigning your own `out` parameters, so `TryParse` style APIs work.
- Top-level statements (they're host code).

## Set up a core project

Import the shared props file into each functional core project:

```xml
<Import Project="..\FunctionalLint\build\FunctionalCore.props" />
```

The props file does four things:
- Adds the analyzer and its code fixes.
- Turns on nullable reference types and makes nullable warnings errors.
- Makes non-exhaustive switch expressions (CS8509) errors, so unions and closed hierarchies are checked properly.
- Loads `functional.globalconfig`, where you set the legacy namespaces and any extra side-effecting APIs.

Leave the props file out of host, test and adapter-only projects.

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

An adapter takes legacy services in its constructor. Its public members return only functional types, which FP0009 checks.

```csharp
[ImperativeAdapter]
public sealed class Orders(OrderService service)
{
    public Result<ImmutableArray<Order>> Open()
    {
        try
        {
            return new Ok<ImmutableArray<Order>>(
                service.GetOpenOrders().Select(Order.From).ToImmutableArray());
        }
        catch (SqlException ex)
        {
            return new Unavailable(ex.Message);
        }
    }
}
```

Use the same pattern for side effects. Wrap `TimeProvider`, `HttpClient` or a `DbContext` in an adapter, or pass values like "now" in as parameters.

## Tune it

Two settings live in `functional.globalconfig`:
- `functional_lint.legacy_namespaces`: comma-separated namespaces of existing imperative code (FP0006, FP0009).
- `functional_lint.impure_apis`: extra side-effecting APIs for FP0011, as a type (`Acme.Mailer`) or one member (`Acme.Mailer.Send`).

Change any rule's severity in `functional.globalconfig` or `.editorconfig`:

```ini
dotnet_diagnostic.FP0008.severity = warning
```

For a one-off, use `#pragma warning disable FP0002` around the line.

## Fix and learn: the agent loop

Some rules have a mechanical fix. `heal.sh` builds the project, applies those fixes, builds again, and prints what it fixed, the habit that avoids each one, and what's left:

```
$ FunctionalLint/heal.sh src/Core/Core.csproj
== Auto-fixed
  FP0003 x3. Next time: Declare fields 'readonly', properties '{ get; init; }', and 'readonly record struct'.
  FP0004 x2. Next time: Write records for data and static classes for functions.
  FP0008 x2. Next time: Take and return IReadOnlyList / IReadOnlyDictionary / ImmutableArray, never List or IList.
  FP0010 x1. Next time: Make async methods return Task, never void.

== Remaining: 3 error(s) to fix by hand
Orders.cs(19,9): error FP0001: 'foreach' loops are not allowed. Use LINQ, Aggregate or recursion instead.
...
```

It exits 0 only when the project builds clean, so it works as a pre-commit hook or an agent's "am I done?" check.

When to run what:
- **After each file write:** `dotnet build --no-restore`. The analyzer adds little; on a 200 KB file it takes about 0.3 s. The compiler is most of the time.
- **Before commit:** `heal.sh`. Each `dotnet format` pass loads the whole project, so allow 5-10 seconds per fixable rule that fired.

Auto-fixes only make changes that keep the code's meaning. Where that isn't possible (loops, mutation, throws, side effects) the message says what to write instead. If a fix makes other code stop compiling, that code was mutating something it shouldn't, and the compiler now points at it.

To fix things one at a time in the IDE, use the light bulb on the error.

## Run the tests

```
dotnet test
```

The tests are laid out so it's clear where a new case goes:

| Folder | Checks |
|--------|--------|
| `Rules/` | One file per rule: what it reports, exactly where, and what it allows. |
| `Fixes/` | Each code fix: before and after, including the cases it deliberately leaves alone. |
| `Guidance/MessageTests.cs` | The exact text of every message, and that each one names the problem and the remedy on one short line. |
| `Guidance/RuleCatalogTests.cs` | Rules are well formed, and the code, README, rule docs and tests agree. |
| `Guidance/RobustnessTests.cs` | Half-written code never crashes the analyzer, and a large file is analysed quickly. |

In test sources, `{|FP0001:text|}` means "expect FP0001 on exactly this text". Any diagnostic that isn't marked fails the test.

## What it can't catch

- **Mutating calls on things you were handed.** For example `Array.Sort(xs)`, or a method on a mutable type from a library. FP0005 and FP0008 stop most mutable collections from getting in, which covers most cases.
- **Mutation inside libraries you call.**
- **Arrays in signatures.** Arrays are allowed, but writing to an element is flagged.

## A warning about recursion

C# doesn't guarantee tail calls, so deep recursion can overflow the stack. Use LINQ (`Select`, `Where`, `Aggregate`) for loop-like work. Keep recursion for shallow structures such as trees.
