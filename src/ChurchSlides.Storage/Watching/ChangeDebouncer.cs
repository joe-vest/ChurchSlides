namespace ChurchSlides.Storage.Watching;

/// <summary>
/// Collects file events and releases each path once it has been quiet for a while (LIB-007, decision 3).
/// Driven by time values so tests need no sleeps. Not thread-safe: the caller locks.
/// </summary>
public sealed class ChangeDebouncer(TimeSpan? quietPeriod = null)
{
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(500);

    private readonly TimeSpan _quiet = quietPeriod ?? DefaultQuietPeriod;
    private readonly Dictionary<string, DateTimeOffset> _lastEvent = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Any event for the path restarts its quiet period.</summary>
    public void Add(string path, DateTimeOffset at) => _lastEvent[path] = at;

    /// <summary>Removes and returns the paths whose quiet period has ended.</summary>
    public IReadOnlyList<string> TakeDue(DateTimeOffset now)
    {
        var due = _lastEvent.Where(p => now - p.Value >= _quiet).Select(p => p.Key).ToList();
        foreach (var path in due) _lastEvent.Remove(path);
        return due;
    }

    /// <summary>When the next path becomes due, or null when nothing is waiting.</summary>
    public DateTimeOffset? NextDueAt => _lastEvent.Count == 0 ? null : _lastEvent.Values.Min() + _quiet;
}
