using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Functional;

/// <summary>
/// Lets System.Text.Json read and write <see cref="ValueList{T}"/> as a JSON array and <see cref="ValueMap{TKey,TValue}"/>
/// as a JSON object. Without it, writing works but reading throws, because both are read-only collections. JSON null
/// and a missing property both read as empty. Applied with [JsonConverter], so no setup is needed.
/// </summary>
[ImperativeAdapter]
public sealed class ValueListJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() is var definition
        && (definition == typeof(ValueList<>) || definition == typeof(ValueMap<,>));

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter?)Activator.CreateInstance(
            (typeToConvert.GetGenericTypeDefinition() == typeof(ValueList<>) ? typeof(Converter<>) : typeof(MapConverter<,>))
                .MakeGenericType(typeToConvert.GetGenericArguments()));

    private sealed class MapConverter<TKey, TValue> : JsonConverter<ValueMap<TKey, TValue>> where TKey : notnull
    {
        public override bool HandleNull => true;

        public override ValueMap<TKey, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null
                ? default
                : new ValueMap<TKey, TValue>(JsonSerializer.Deserialize<ImmutableDictionary<TKey, TValue>>(ref reader, options)
                    ?? ImmutableDictionary<TKey, TValue>.Empty);

        public override void Write(Utf8JsonWriter writer, ValueMap<TKey, TValue> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Items, options);
    }

    private sealed class Converter<T> : JsonConverter<ValueList<T>>
    {
        public override bool HandleNull => true;

        public override ValueList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null
                ? default
                : new ValueList<T>(JsonSerializer.Deserialize<ImmutableArray<T>>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, ValueList<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Items, options);
    }
}
