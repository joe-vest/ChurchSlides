using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchSlides.Domain;

/// <summary>
/// A typed identifier. <c>Id&lt;Show&gt;</c> and <c>Id&lt;Slide&gt;</c> are different types,
/// so they can't be swapped by accident. Serialized as a plain GUID string.
/// </summary>
[JsonConverter(typeof(IdJsonConverterFactory))]
public readonly record struct Id<T>(Guid Value)
{
    public static Id<T> New() => new(Guid.NewGuid());

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}

/// <summary>Converts any <see cref="Id{T}"/> to and from a GUID string.</summary>
public sealed class IdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Id<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(IdJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class IdJsonConverter<T> : JsonConverter<Id<T>>
    {
        public override Id<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetGuid());

        public override void Write(Utf8JsonWriter writer, Id<T> value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }
}
