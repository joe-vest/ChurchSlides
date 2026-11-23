using ChurchSlides.Domain.Time;

namespace ChurchSlides.Storage.Watching;

/// <summary>
/// The only part of Library watching that touches the real folder: feeds <see cref="FileSystemWatcher"/> events into the
/// debouncer and, on a timer, hands the quiet paths to the <see cref="LibraryMonitor"/>. Events arrive on a background thread
/// (decision 8). It polls for the folder disappearing and coming back, because watcher errors are not reliable for that.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    private readonly LibraryMonitor _monitor;
    private readonly IClock _clock;
    private readonly ChangeDebouncer _debouncer;
    private readonly object _gate = new();
    private Timer? _timer;
    private FileSystemWatcher? _watcher;
    private bool _available;
    private bool _needsRescan;
    private bool _disposed;
    private int _ticking;

    public FolderWatcher(LibraryMonitor monitor, IClock clock, TimeSpan? quietPeriod = null)
    {
        _monitor = monitor;
        _clock = clock;
        _debouncer = new ChangeDebouncer(quietPeriod);
    }

    /// <summary>Something changed. Raised on a background thread.</summary>
    public event Action<LibraryUpdate>? Updated;

    /// <summary>The Library folder went away. Raised once; <see cref="Updated"/> follows when it is back and differs.</summary>
    public event Action? Unavailable;

    /// <summary>Starts watching. The caller runs <see cref="LibraryMonitor.Scan"/> first for the startup state.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_timer is not null || _disposed) return;
            _available = Directory.Exists(_monitor.Layout.Root);
            if (_available) CreateWatcher();
            _timer = new Timer(_ => OnTick(), null, Tick, Tick);
        }
    }

    private void CreateWatcher()
    {
        var watcher = new FileSystemWatcher(_monitor.Layout.Root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Created += (_, e) => Queue(e.FullPath);
        watcher.Changed += (_, e) => Queue(e.FullPath);
        watcher.Deleted += (_, e) => Queue(e.FullPath);
        watcher.Renamed += (_, e) => { Queue(e.OldFullPath); Queue(e.FullPath); };
        watcher.Error += (_, _) => { lock (_gate) _needsRescan = true; }; // buffer overflow: look at everything
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    private void Queue(string path)
    {
        lock (_gate) _debouncer.Add(path, _clock.Now);
    }

    private void OnTick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return; // a slow pass is still running
        try
        {
            TickOnce();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate) _needsRescan = true; // a sync tool held something; look again next tick
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private void TickOnce()
    {
        var exists = Directory.Exists(_monitor.Layout.Root);
        IReadOnlyList<string> due;
        bool rescan;
        bool wasAvailable;
        lock (_gate)
        {
            if (_disposed) return;
            wasAvailable = _available;
            if (!exists && _available)
            {
                _available = false;
                DisposeWatcher();
            }
            else if (exists && !_available)
            {
                _available = true;
                _needsRescan = true;
                CreateWatcher();
            }
            rescan = _needsRescan && _available;
            if (rescan) _needsRescan = false;
            due = _debouncer.TakeDue(_clock.Now);
        }

        if (!exists)
        {
            if (wasAvailable) Unavailable?.Invoke();
            return;
        }

        var update = rescan ? _monitor.Rescan() : due.Count > 0 ? _monitor.Apply(due) : null;
        if (update is { IsEmpty: false }) Updated?.Invoke(update);
    }

    private void DisposeWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            DisposeWatcher();
        }
        _timer?.Dispose();
    }
}
