using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchSlides.Domain;

/// <summary>
/// A path inside the Library, always relative and always using forward slashes (LIB-003).
/// Absolute paths, drive letters, UNC paths and ".." can't be represented.
/// </summary>
[JsonConverter(typeof(LibraryRelativePathJsonConverter))]
public readonly record struct LibraryRelativePath
{
    private static readonly char[] InvalidSegmentChars = ['<', '>', '"', '|', '?', '*', ':', '\0'];

    public string Value { get; }

    private LibraryRelativePath(string value) => Value = value;

    public static bool TryCreate(string? input, out LibraryRelativePath path)
    {
        path = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var normalized = input.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/')) return false; // absolute or UNC

        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..") return false;
            if (segment.IndexOfAny(InvalidSegmentChars) >= 0) return false; // includes drive letters
        }

        path = new LibraryRelativePath(normalized);
        return true;
    }

    public static LibraryRelativePath Create(string? input) =>
        TryCreate(input, out var path)
            ? path
            : throw new ArgumentException($"Not a valid Library-relative path: '{input}'.", nameof(input));

    /// <summary>The full path of this file under the given Library root folder.</summary>
    public string ResolveUnder(string libraryRoot) =>
        Path.GetFullPath(Path.Combine(libraryRoot, (Value ?? "").Replace('/', Path.DirectorySeparatorChar)));

    public override string ToString() => Value ?? "";
}

public sealed class LibraryRelativePathJsonConverter : JsonConverter<LibraryRelativePath>
{
    public override LibraryRelativePath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return LibraryRelativePath.TryCreate(text, out var path)
            ? path
            : throw new JsonException($"Not a valid Library-relative path: '{text}'.");
    }

    public override void Write(Utf8JsonWriter writer, LibraryRelativePath value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
