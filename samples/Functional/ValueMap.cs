using System.Collections;
using System.Collections.Immutable;

namespace Functional;

/// <summary>
/// An immutable dictionary that is equal to another with the same keys and values. Use it in records instead of
/// <see cref="ImmutableDictionary{TKey,TValue}"/>, which compares by reference. <c>default</c> is empty.
/// Lookups never throw: use <see cref="Find"/>, <see cref="TryGetValue"/> or <see cref="GetValueOrDefault"/>.
/// </summary>
[ValueEquality]
[System.Text.Json.Serialization.JsonConverter(typeof(ValueListJsonConverter))]
public readonly struct ValueMap<TKey, TValue> : IReadOnlyCollection<KeyValuePair<TKey, TValue>>, IEquatable<ValueMap<TKey, TValue>>
    where TKey : notnull
{
    private readonly ImmutableDictionary<TKey, TValue>? _items;

    public ValueMap(ImmutableDictionary<TKey, TValue> items)
    {
        _items = items;
    }

    public ImmutableDictionary<TKey, TValue> Items => _items ?? ImmutableDictionary<TKey, TValue>.Empty;

    public int Count => Items.Count;
    public bool IsEmpty => Items.IsEmpty;
    public IEnumerable<TKey> Keys => Items.Keys;
    public IEnumerable<TValue> Values => Items.Values;

    public bool ContainsKey(TKey key) => Items.ContainsKey(key);
    public bool TryGetValue(TKey key, out TValue value) => Items.TryGetValue(key, out value!);
    public TValue? GetValueOrDefault(TKey key) => Items.GetValueOrDefault(key);
    public Option<TValue> Find(TKey key) => Items.TryGetValue(key, out var value) ? Option.Some(value) : Option.None<TValue>();

    /// <summary>Adds or replaces. (ImmutableDictionary.Add throws if the key is there with another value.)</summary>
    public ValueMap<TKey, TValue> SetItem(TKey key, TValue value) => new(Items.SetItem(key, value));
    public ValueMap<TKey, TValue> Remove(TKey key) => new(Items.Remove(key));

    public bool Equals(ValueMap<TKey, TValue> other) =>
        Count == other.Count
        && Items.All(pair => other.Items.TryGetValue(pair.Key, out var value) && EqualityComparer<TValue>.Default.Equals(pair.Value, value));
    public override bool Equals(object? obj) => obj is ValueMap<TKey, TValue> other && Equals(other);
    // Order-independent, so two maps built in different orders hash the same.
    public override int GetHashCode() => Items.Aggregate(Count, (hash, pair) => hash ^ HashCode.Combine(pair.Key, pair.Value));
    public static bool operator ==(ValueMap<TKey, TValue> left, ValueMap<TKey, TValue> right) => left.Equals(right);
    public static bool operator !=(ValueMap<TKey, TValue> left, ValueMap<TKey, TValue> right) => !left.Equals(right);

    public override string ToString() => "{" + string.Join(", ", Items.Select(pair => $"{pair.Key}: {pair.Value}")) + "}";

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator ValueMap<TKey, TValue>(ImmutableDictionary<TKey, TValue> items) => new(items);
    public static implicit operator ImmutableDictionary<TKey, TValue>(ValueMap<TKey, TValue> map) => map.Items;
}

public static class ValueMap
{
    public static ValueMap<TKey, TValue> Empty<TKey, TValue>() where TKey : notnull => default;

    /// <summary>Builds a map. If two items share a key, the last one wins (it never throws).</summary>
    public static ValueMap<TKey, TValue> ToValueMap<T, TKey, TValue>(
        this IEnumerable<T> items, Func<T, TKey> key, Func<T, TValue> value) where TKey : notnull =>
        new(items.GroupBy(key).ToImmutableDictionary(group => group.Key, group => value(group.Last())));
}
