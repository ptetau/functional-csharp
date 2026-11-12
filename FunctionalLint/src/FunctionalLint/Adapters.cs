using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace FunctionalLint.Adapters;

// FunctionalLint is checked by its own rules (functionallint.policy lists it as a core). This file is its imperative
// part: the only mutable state (caches shared by callbacks running in parallel) and the only loop over side effects.

/// <summary>Marks what the rules skip. Matched by name, like the copy a consuming project defines.</summary>
[AttributeUsage(AttributeTargets.All)]
internal sealed class ImperativeAdapterAttribute : Attribute;

/// <summary>
/// A thread-safe cache for answers that don't change within a compilation: whether a symbol is exempt, which
/// namespaces a file treats as legacy, whether a library is allowed. Callbacks run in parallel, so it's concurrent.
/// </summary>
[ImperativeAdapter]
internal sealed class Memo<TKey, TValue>(IEqualityComparer<TKey> comparer) where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _values = new(comparer);

    public TValue Get(TKey key, Func<TKey, TValue> compute) => _values.GetOrAdd(key, compute);
}

[ImperativeAdapter]
internal static class Memo
{
    public static Memo<TKey, TValue> Of<TKey, TValue>(IEqualityComparer<TKey> comparer) where TKey : notnull => new(comparer);

    public static Memo<TSymbol, TValue> OfSymbols<TSymbol, TValue>() where TSymbol : ISymbol =>
        new(new SymbolComparer<TSymbol>());

    private sealed class SymbolComparer<TSymbol> : IEqualityComparer<TSymbol> where TSymbol : ISymbol
    {
        public bool Equals(TSymbol? x, TSymbol? y) => SymbolEqualityComparer.Default.Equals(x, y);
        public int GetHashCode(TSymbol obj) => SymbolEqualityComparer.Default.GetHashCode(obj);
    }
}

[ImperativeAdapter]
internal static class Effects
{
    /// <summary>Reports each diagnostic: Roslyn takes them one call at a time.</summary>
    public static void ReportAll(IEnumerable<Diagnostic> diagnostics, Action<Diagnostic> report)
    {
        foreach (var diagnostic in diagnostics) report(diagnostic);
    }
}

[ImperativeAdapter]
internal static class Sequences
{
    /// <summary>
    /// <paramref name="first"/>, then <paramref name="next"/> of it, and so on until null: an operation and its
    /// parents, say. A primitive like LINQ's own operators, written with the loop they hide. Lazy.
    /// </summary>
    public static IEnumerable<T> Unfold<T>(T? first, Func<T, T?> next) where T : class
    {
        for (var current = first; current is not null; current = next(current)) yield return current;
    }
}
