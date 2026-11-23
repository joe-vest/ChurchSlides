using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChurchSlides.Domain.Serialization;
using ChurchSlides.Storage.Migration;

namespace ChurchSlides.Storage;

/// <summary>The shared file logic behind every repository: load with migration, and save with the safety checks (LIB-004..006).</summary>
internal sealed class LibraryFileStore(AtomicFileWriter writer, MigrationChain migrations)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Null when the file doesn't exist.</summary>
    private static (byte[] Bytes, FileStamp Stamp)? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            return (bytes, FileStamp.FromBytes(bytes, File.GetLastWriteTimeUtc(path)));
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public LoadedFile<T>? Load<T>(string path, Func<T, Guid>? idOf = null) where T : class
    {
        (byte[] Bytes, FileStamp Stamp)? file;
        try { file = Read(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(new(LoadStatus.Unreadable, 0, null, $"{Path.GetFileName(path)}: {ex.Message}"), null, path, true);
        }
        if (file is null) return null;

        var result = Parse<T>(file.Value.Bytes);
        if (result.Status == LoadStatus.Unreadable)
            result = result with { Error = $"{Path.GetFileName(path)}: {result.Error}" };

        var nameMatches = true;
        if (idOf is not null && result.Value is not null)
            nameMatches = string.Equals(Path.GetFileNameWithoutExtension(path), idOf(result.Value).ToString(), StringComparison.OrdinalIgnoreCase);

        return new(result, file.Value.Stamp, path, nameMatches);
    }

    private LoadResult<T> Parse<T>(byte[] bytes) where T : class
    {
        var version = ReadVersion(bytes, out var envelope);
        var current = SchemaVersions.Current<T>();
        if (envelope is null || version < 1 || version >= current)
            return VersionedFile.Deserialize<T>(bytes); // also reports damaged and newer files

        var outcome = migrations.Migrate(envelope, typeof(T), version, current);
        if (!outcome.Ok)
            return new(LoadStatus.Unreadable, version, null, outcome.Error);

        var migrated = VersionedFile.Deserialize<T>(Encoding.UTF8.GetBytes(envelope.ToJsonString()));
        return migrated with { FileVersion = version };
    }

    /// <summary>The envelope's schema version, or 0 when the bytes aren't a ChurchSlides file.</summary>
    private static int ReadVersion(byte[] bytes, out JsonObject? envelope)
    {
        envelope = null;
        try
        {
            if (JsonNode.Parse(bytes) is not JsonObject obj) return 0;
            envelope = obj;
            return obj["schemaVersion"] is JsonValue v && v.TryGetValue<int>(out var version) ? version : 0;
        }
        catch (JsonException) { return 0; }
    }

    /// <param name="expected">The stamp from when the object was loaded. Null means the file must not exist yet.</param>
    public SaveResult Save<T>(string path, T value, FileStamp? expected) where T : class
    {
        try
        {
            var bytes = Utf8NoBom.GetBytes(VersionedFile.Serialize(value));

            var disk = Read(path);
            if (disk is null)
            {
                if (expected is not null) return new(SaveOutcome.Conflict, Error: "The file was deleted elsewhere since it was loaded.");
            }
            else
            {
                if (ReadVersion(disk.Value.Bytes, out _) > SchemaVersions.Current<T>())
                    return new(SaveOutcome.ReadOnlyNewer, DiskStamp: disk.Value.Stamp,
                        Error: "The file was saved by a newer version of ChurchSlides and can't be changed by this one.");
                if (!disk.Value.Stamp.SameContentAs(expected))
                    return new(SaveOutcome.Conflict, DiskStamp: disk.Value.Stamp, Error: "The file was changed elsewhere since it was loaded.");
                if (disk.Value.Bytes.AsSpan().SequenceEqual(bytes))
                    return new(SaveOutcome.Unchanged, Stamp: disk.Value.Stamp);
            }

            var written = writer.Write(path, bytes);
            return new(SaveOutcome.Saved, Stamp: FileStamp.FromBytes(bytes, written));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(SaveOutcome.IoFailed, Error: ex.Message);
        }
    }

    public SaveResult Delete<T>(string path, FileStamp? expected) where T : class
    {
        try
        {
            var disk = Read(path);
            if (disk is null) return new(SaveOutcome.Unchanged);
            if (ReadVersion(disk.Value.Bytes, out _) > SchemaVersions.Current<T>())
                return new(SaveOutcome.ReadOnlyNewer, DiskStamp: disk.Value.Stamp,
                    Error: "The file was saved by a newer version of ChurchSlides and can't be deleted by this one.");
            if (!disk.Value.Stamp.SameContentAs(expected))
                return new(SaveOutcome.Conflict, DiskStamp: disk.Value.Stamp, Error: "The file was changed elsewhere since it was loaded.");

            writer.Delete(path);
            return new(SaveOutcome.Deleted);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(SaveOutcome.IoFailed, Error: ex.Message);
        }
    }
}
