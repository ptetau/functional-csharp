using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Functional;

/// <summary>
/// Marks a collection type whose Equals compares contents. FunctionalLint's FP0014 accepts collections of these
/// types inside records; it matches the attribute by name, so any library can define its own.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ValueEqualityAttribute : Attribute;

/// <summary>
/// An immutable list that is equal to another list with the same items in the same order.
/// Use it in records instead of <see cref="ImmutableArray{T}"/>, which compares by reference, so a record
/// holding one is never equal to a copy of itself. <c>default</c> is the empty list. It converts to and from
/// <see cref="ImmutableArray{T}"/> implicitly, and collection expressions work: <c>ValueList&lt;int&gt; xs = [1, 2];</c>
/// </summary>
[ValueEquality]
[CollectionBuilder(typeof(ValueList), nameof(ValueList.Create))]
[System.Text.Json.Serialization.JsonConverter(typeof(ValueListJsonConverter))]
public readonly struct ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly ImmutableArray<T> _items;

    public ValueList(ImmutableArray<T> items)
    {
        _items = items;
    }

    /// <summary>The items. Never a default ImmutableArray, so it's always safe to use.</summary>
    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Count => Items.Length;
    public int Length => Items.Length;
    public bool IsEmpty => Items.IsEmpty;
    public T this[int index] => Items[index];

    public ValueList<T> Add(T item) => new(Items.Add(item));
    public ValueList<T> AddRange(IEnumerable<T> items) => new(Items.AddRange(items));
    public ValueList<T> Insert(int index, T item) => new(Items.Insert(index, item));
    public ValueList<T> SetItem(int index, T item) => new(Items.SetItem(index, item));
    public ValueList<T> Remove(T item) => new(Items.Remove(item));
    public ValueList<T> RemoveAt(int index) => new(Items.RemoveAt(index));

    // Instance methods win over LINQ's extension methods. LINQ over a struct that implements IEnumerable boxes it and
    // allocates an enumerator on every call (92 MB per render in the ray tracer); these go straight to the array.
    public TAcc Aggregate<TAcc>(TAcc seed, Func<TAcc, T, TAcc> step) => Items.Aggregate(seed, step);
    public TOut Aggregate<TAcc, TOut>(TAcc seed, Func<TAcc, T, TAcc> step, Func<TAcc, TOut> result) => Items.Aggregate(seed, step, result);
    public bool Any() => !Items.IsEmpty;
    public bool Any(Func<T, bool> predicate) => Items.Any(predicate);
    public bool All(Func<T, bool> predicate) => Items.All(predicate);
    public IEnumerable<TOut> Select<TOut>(Func<T, TOut> selector) => Items.Select(selector);
    public IEnumerable<T> Where(Func<T, bool> predicate) => Items.Where(predicate);

    /// <summary>Makes list patterns with a slice work: <c>xs is [var first, .. var rest]</c>.</summary>
    public ValueList<T> Slice(int start, int length) => new(Items.Slice(start, length));

    public bool Equals(ValueList<T> other) => Items.SequenceEqual(other.Items);
    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);
    public override int GetHashCode() => Items.Aggregate(19, (hash, item) => HashCode.Combine(hash, item));
    public static bool operator ==(ValueList<T> left, ValueList<T> right) => left.Equals(right);
    public static bool operator !=(ValueList<T> left, ValueList<T> right) => !left.Equals(right);

    /// <summary>Shows the items, so a failed test prints what it compared.</summary>
    public override string ToString() => "[" + string.Join(", ", Items) + "]";

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    public static implicit operator ValueList<T>(ImmutableArray<T> items) => new(items);
    public static implicit operator ImmutableArray<T>(ValueList<T> list) => list.Items;
}

public static class ValueList
{
    public static ValueList<T> Create<T>(ReadOnlySpan<T> items) => new(ImmutableArray.Create(items));

    public static ValueList<T> Empty<T>() => default;

    public static ValueList<T> ToValueList<T>(this IEnumerable<T> items) => new(items.ToImmutableArray());
}
