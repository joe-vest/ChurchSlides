using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchSlides.Storage.Local;

/// <summary>
/// Machine-local data: never inside the Library and never synced (LIB-008, ARCH 9.1).
/// Only <see cref="SettingsFile"/> and <see cref="DraftsFolder"/> are used so far. Folders are created when something writes to them.
/// </summary>
public sealed class LocalDataPaths
{
    public LocalDataPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public static LocalDataPaths Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChurchSlides"));

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string ImageCacheFolder => Path.Combine(Root, "cache", "images");
    public string BibleCacheFolder => Path.Combine(Root, "cache", "bibles");
    public string SearchIndexFile => Path.Combine(Root, "cache", "search.idx");
    public string DraftsFolder => Path.Combine(Root, "drafts");
    public string LogFile => Path.Combine(Root, "logs", "app.log");
    public string LastSessionFile => Path.Combine(Root, "last-session.json");
}

/// <summary>The one machine-local setting so far. The Library path is the only place an absolute path may be stored (LIB-001).</summary>
public sealed record LocalSettings
{
    public string? LibraryPath { get; init; }
}

public interface ILocalSettingsStore
{
    /// <summary>A missing, damaged or unreadable file gives defaults. Never throws.</summary>
    LocalSettings Load();

    SaveResult Save(LocalSettings settings);
}

public sealed class LocalSettingsStore(LocalDataPaths paths, AtomicWriteOptions? writeOptions = null) : ILocalSettingsStore
{
    private readonly AtomicFileWriter _writer = new(writeOptions);

    public LocalSettings Load()
    {
        var bytes = LocalFiles.TryRead(paths.SettingsFile);
        if (bytes is null) return new();
        try
        {
            var file = JsonSerializer.Deserialize(bytes, LocalJsonContext.Default.LocalSettingsFile);
            return new() { LibraryPath = file?.LibraryPath };
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public SaveResult Save(LocalSettings settings)
    {
        var file = new LocalSettingsFile { LibraryPath = settings.LibraryPath };
        return LocalFiles.Write(_writer, paths.SettingsFile, JsonSerializer.SerializeToUtf8Bytes(file, LocalJsonContext.Default.LocalSettingsFile));
    }
}

internal sealed record LocalSettingsFile
{
    public int SchemaVersion { get; init; } = 1;
    public string? LibraryPath { get; init; }
}

internal static class LocalFiles
{
    /// <summary>Null when the file is missing or can't be read.</summary>
    public static byte[]? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SaveResult Write(AtomicFileWriter writer, string path, byte[] bytes)
    {
        try
        {
            var written = writer.Write(path, bytes);
            return new(SaveOutcome.Saved, Stamp: FileStamp.FromBytes(bytes, written));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(SaveOutcome.IoFailed, Error: ex.Message);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LocalSettingsFile))]
[JsonSerializable(typeof(DraftFile))]
internal sealed partial class LocalJsonContext : JsonSerializerContext;
