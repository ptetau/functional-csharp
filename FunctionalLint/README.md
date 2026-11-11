# FunctionalLint

A Roslyn analyzer that keeps a C# project functional. It reports errors at build time and shows squiggles in the IDE.

## Rules

| Rule   | Forbids | Do this instead |
|--------|---------|-----------------|
| FP0001 | `for`, `foreach`, `while`, `do` | LINQ, `Aggregate`, recursion |
| FP0002 | Reassigning variables, `+=`, `++`, setting fields, array writes, event `+=` | New values, `with` expressions |
| FP0003 | Non-readonly fields, `set` accessors, non-readonly record structs | `readonly`, `init`, `readonly record struct` |
| FP0004 | Plain classes | Records for data, static classes for functions |
| FP0005 | Creating `List<T>`, `Dictionary<,>`, `StringBuilder` and similar | `System.Collections.Immutable` types |
| FP0006 | Calling legacy code directly | Call it through an `[ImperativeAdapter]` |
| FP0007 | `throw` | Return an `Error` on your `Result<T>` |
| FP0008 | `IList<T>`, `ICollection<T>`, `IDictionary<,>`, `List<T>` etc. in signatures | `IReadOnlyList<T>`, `ImmutableArray<T>` |
| FP0009 | Adapters with legacy types in their public signatures | Return functional types |
| FP0010 | `async void` | Return `Task` |
| FP0011 | Clock, `Guid.NewGuid`, `Random`, `Console`, file I/O, `HttpClient` and similar | An adapter, or pass the value in |

These are still allowed:
- Object initialisers and `with` expressions, because they build a new value.
- Deconstruction into new variables.
- Constructors and `init` accessors setting their own type's members.
- Attribute classes.
- `out` and `ref` parameters, so `TryParse` style APIs work.

## Set up a core project

Import the shared props file into each functional core project:

```xml
<Import Project="..\FunctionalLint\build\FunctionalCore.props" />
```

The props file does four things:
- Adds the analyzer.
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

Change any rule's severity in `functional.globalconfig` or `.editorconfig`:

```ini
dotnet_diagnostic.FP0008.severity = warning
```

For a one-off, use `#pragma warning disable FP0002` around the line.

## Run the tests

```
dotnet test tests/FunctionalLint.Tests
```

## What it can't catch

- **Mutating calls on things you were handed.** For example `Array.Sort(xs)`, or a method on a mutable type from a library. FP0005 and FP0008 stop most mutable collections from getting in, which covers most cases.
- **Mutation inside libraries you call.**
- **Locals typed as mutable interfaces.** FP0008 checks signatures, not local variables.
- **Arrays in signatures.** Arrays are allowed, but writing to an element is flagged.

## A warning about recursion

C# doesn't guarantee tail calls, so deep recursion can overflow the stack. Use LINQ (`Select`, `Where`, `Aggregate`) for loop-like work. Keep recursion for shallow structures such as trees.
