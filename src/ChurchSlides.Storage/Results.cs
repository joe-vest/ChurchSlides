using System.Security.Cryptography;
using ChurchSlides.Domain.Serialization;

namespace ChurchSlides.Storage;

/// <summary>
/// What a file looked like when it was loaded (LIB-006). Saving hands it back so the repository can tell
/// whether the file changed on disk in the meantime. Only <see cref="Sha256"/> decides; the write time is a
/// cheap hint for the folder watcher.
/// </summary>
public sealed record FileStamp(DateTime LastWriteUtc, string Sha256)
{
    public static FileStamp FromBytes(ReadOnlySpan<byte> bytes, DateTime lastWriteUtc) =>
        new(lastWriteUtc, Convert.ToHexString(SHA256.HashData(bytes)));

    public bool SameContentAs(FileStamp? other) =>
        other is not null && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal);
}

/// <summary>
/// A loaded file. <see cref="Stamp"/> is null only for a singleton file that doesn't exist yet.
/// <see cref="NameMatchesId"/> is false only when the file loaded and its Id differs from its file name,
/// which C1.4 reports as a possible copied file (LIB-009).
/// </summary>
public sealed record LoadedFile<T>(LoadResult<T> Result, FileStamp? Stamp, string Path, bool NameMatchesId)
    where T : class;

public enum SaveOutcome
{
    Saved,
    Deleted,

    /// <summary>The file already holds exactly this content, so nothing was written.</summary>
    Unchanged,

    /// <summary>The file changed on disk since it was loaded (LIB-006), or exists when a new object was expected.</summary>
    Conflict,

    /// <summary>The file on disk is from a newer version of the app and must not be touched (LIB-004).</summary>
    ReadOnlyNewer,

    /// <summary>The object failed validation. Nothing was written.</summary>
    Invalid,

    /// <summary>The disk refused the write after retries.</summary>
    IoFailed,
}

public sealed record SaveResult(
    SaveOutcome Outcome,
    FileStamp? Stamp = null,
    FileStamp? DiskStamp = null,
    IReadOnlyList<string>? Problems = null,
    string? Error = null)
{
    public bool Succeeded => Outcome is SaveOutcome.Saved or SaveOutcome.Unchanged or SaveOutcome.Deleted;
}
