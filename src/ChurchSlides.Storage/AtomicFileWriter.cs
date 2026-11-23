namespace ChurchSlides.Storage;

public sealed record AtomicWriteOptions
{
    public int Attempts { get; init; } = 5;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Called with the finished temp file's path, just before it replaces the target. Tests use it to simulate a crash or a lock.</summary>
    public Action<string>? BeforeCommit { get; init; }
}

/// <summary>
/// Writes a file so that readers (and a sync tool) only ever see the old content or the complete new content (LIB-005).
/// The content goes to <c>{name}.saving</c> first, then replaces the target. Retries cover a sync tool briefly holding the file.
/// </summary>
public sealed class AtomicFileWriter(AtomicWriteOptions? options = null)
{
    public const string TempExtension = ".saving";

    private readonly AtomicWriteOptions _options = options ?? new();

    /// <summary>Returns the target's last-write time (UTC). Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when every attempt failed.</summary>
    public DateTime Write(string path, ReadOnlySpan<byte> bytes)
    {
        var content = bytes.ToArray();
        WithRetries(() => WriteOnce(path, content));
        return File.GetLastWriteTimeUtc(path);
    }

    public void Delete(string path) => WithRetries(() => File.Delete(path));

    private void WriteOnce(string path, byte[] bytes)
    {
        var temp = path + TempExtension;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            _options.BeforeCommit?.Invoke(temp);

            if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null);
            else File.Move(temp, path);
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private void WithRetries(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < _options.Attempts)
            {
                Thread.Sleep(_options.RetryDelay);
            }
        }
    }
}
