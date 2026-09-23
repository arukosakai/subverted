namespace Subverted.Daemon;

/// <summary>
/// A <see cref="FileSystemWatcher"/> reduced to one signal and the path it happened at. Telling
/// the event *kinds* apart still buys nothing — a session responds to all of them identically —
/// and missing one costs a wrong answer, so the design is about never losing an event quietly (D5).
/// </summary>
public sealed class FileSystemChangeNotifier : IChangeNotifier
{
    // The documented ceiling for the kernel-side buffer. Raising it does not stop overflows during
    // an asset import; it only makes them rarer, which is why the Error path matters more.
    private const int MaxBufferBytes = 64 * 1024;

    private readonly FileSystemWatcher? _watcher;
    private readonly string _root;
    private volatile bool _disposed;

    public event Action<WorkingCopyChange>? Changed;

    /// <summary>Why this notifier is not watching, or <c>null</c> when it is.</summary>
    public string? UnavailableReason { get; private set; }

    public bool IsWatching => !_disposed && _watcher is { EnableRaisingEvents: true };

    public FileSystemChangeNotifier(string rootPath)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

        try
        {
            _watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = MaxBufferBytes,
                NotifyFilter =
                    NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.Attributes,
            };

            _watcher.Created += OnFileSystemChange;
            _watcher.Changed += OnFileSystemChange;
            _watcher.Deleted += OnFileSystemChange;
            _watcher.Renamed += OnRename;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Linux runs out of inotify watches long before a game project runs out of directories.
            // Reporting that is the point of this catch; pretending to watch would be the bug.
            _watcher?.Dispose();
            _watcher = null;
            UnavailableReason = ex.Message;
        }
    }

    private void OnFileSystemChange(object sender, FileSystemEventArgs e) =>
        Changed?.Invoke(At(e.FullPath));

    /// <summary>
    /// A rename moves two paths, and the one it moved *from* is the one that now looks missing.
    /// Reporting only the new name leaves the old one reading as though it were still there.
    /// </summary>
    private void OnRename(object sender, RenamedEventArgs e)
    {
        Changed?.Invoke(At(e.OldFullPath));
        Changed?.Invoke(At(e.FullPath));
    }

    /// <summary>
    /// The path as the model spells it. Anything the watcher reports from outside the root cannot
    /// be named in those terms, so it degrades to <see cref="WorkingCopyChange.Unknown"/> rather
    /// than being dropped.
    /// </summary>
    private WorkingCopyChange At(string fullPath)
    {
        var full = Path.GetFullPath(fullPath);
        if (
            !full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)
            || full.Length <= _root.Length + 1
        )
        {
            return WorkingCopyChange.Unknown;
        }

        return new WorkingCopyChange(full[(_root.Length + 1)..].Replace('\\', '/'));
    }

    /// <summary>
    /// The buffer overflowed and events were dropped — exactly what an asset import does. Raising
    /// <see cref="Changed"/> first is what turns the loss into a rescan rather than a stale answer,
    /// and it has to happen before the re-arm in case the re-arm is what fails.
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Unknown, not a path: events were dropped, so no set of paths describes what moved and an
        // incremental update would silently miss whatever was in the discarded buffer.
        Changed?.Invoke(WorkingCopyChange.Unknown);

        try
        {
            _watcher!.EnableRaisingEvents = false;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Left disabled on purpose: IsWatching now reads false, which makes every later
            // request rescan instead of trusting an index nothing is maintaining.
            UnavailableReason = ex.Message;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
    }
}
