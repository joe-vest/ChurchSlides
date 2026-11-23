using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Serialization;

/// <summary>The wrapper around every saved file's content (LIB-004).</summary>
internal sealed record Envelope<T>
{
    public required int SchemaVersion { get; init; }
    public required T Data { get; init; }
}

/// <summary>Reads just the version, ignoring everything else.</summary>
internal sealed record EnvelopeHeader
{
    public int SchemaVersion { get; init; }
}

/// <summary>
/// The current schema version of each file kind. Policy: any change to a saved shape bumps the version,
/// and an older app opens newer files read-only so it never drops fields it doesn't know about.
/// </summary>
public static class SchemaVersions
{
    public const int Show = 1;
    public const int Service = 1;
    public const int Template = 1;
    public const int ServiceTemplate = 1;
    public const int CategoryList = 1;
    public const int LibrarySettings = 1;
    public const int ServiceState = 1;

    public static int Current<T>()
    {
        var type = typeof(T);
        if (type == typeof(Show)) return Show;
        if (type == typeof(Service)) return Service;
        if (type == typeof(Template)) return Template;
        if (type == typeof(ServiceTemplate)) return ServiceTemplate;
        if (type == typeof(CategoryList)) return CategoryList;
        if (type == typeof(LibrarySettings)) return LibrarySettings;
        if (type == typeof(ServiceState)) return ServiceState;
        throw new ArgumentException($"{type.Name} is not a Library file type.");
    }
}

public enum LoadStatus
{
    /// <summary>Loaded, current version.</summary>
    Ok,

    /// <summary>Older than the app understands. A migration must run before use (C1.3).</summary>
    NeedsMigration,

    /// <summary>Newer than the app understands, but readable. Open read-only with a warning.</summary>
    ReadOnlyNewer,

    /// <summary>Damaged, not JSON, or from a newer version in a way that can't be read.</summary>
    Unreadable,
}

public sealed record LoadResult<T>(LoadStatus Status, int FileVersion, T? Value, string? Error)
{
    public bool IsReadOnly => Status == LoadStatus.ReadOnlyNewer;
    public bool HasValue => Value is not null;
}

/// <summary>Turns Library objects into file text and back, wrapped in a versioned envelope.</summary>
public static class VersionedFile
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n", // same bytes on every machine
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep accents and symbols readable
    };

    public static string Serialize<T>(T value) where T : class
    {
        var envelope = new Envelope<T> { SchemaVersion = SchemaVersions.Current<T>(), Data = value };
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            JsonSerializer.Serialize(writer, envelope, EnvelopeInfo<T>());
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    public static LoadResult<T> Deserialize<T>(string text) where T : class =>
        Deserialize<T>(Encoding.UTF8.GetBytes(text));

    public static LoadResult<T> Deserialize<T>(ReadOnlySpan<byte> utf8) where T : class =>
        Deserialize<T>(utf8, SchemaVersions.Current<T>());

    /// <summary>Separate "current version" so tests can simulate older and newer files before a second version exists.</summary>
    internal static LoadResult<T> Deserialize<T>(ReadOnlySpan<byte> utf8, int current) where T : class
    {
        int version;
        try
        {
            var header = JsonSerializer.Deserialize(utf8, ChurchSlidesJsonContext.Default.EnvelopeHeader);
            version = header?.SchemaVersion ?? 0;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new(LoadStatus.Unreadable, 0, null, $"Not a valid ChurchSlides file: {ex.Message}");
        }

        if (version < 1)
            return new(LoadStatus.Unreadable, version, null, "The file has no valid schema version.");

        try
        {
            var envelope = JsonSerializer.Deserialize(utf8, EnvelopeInfo<T>());
            if (envelope is null)
                return new(LoadStatus.Unreadable, version, null, "The file is empty.");

            var status = version == current ? LoadStatus.Ok
                : version > current ? LoadStatus.ReadOnlyNewer
                : LoadStatus.NeedsMigration;
            return new(status, version, envelope.Data, null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var reason = version > current
                ? $"This file was saved by a newer version of ChurchSlides (file version {version}, this app understands {current}). Update the app to open it. {ex.Message}"
                : $"The file can't be read: {ex.Message}";
            return new(LoadStatus.Unreadable, version, null, reason);
        }
    }

    private static JsonTypeInfo<Envelope<T>> EnvelopeInfo<T>() =>
        (JsonTypeInfo<Envelope<T>>)ChurchSlidesJsonContext.Default.GetTypeInfo(typeof(Envelope<T>))!;
}
