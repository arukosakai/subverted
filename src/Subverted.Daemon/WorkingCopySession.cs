using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Daemon;

/// <summary>
/// Keeps one working copy's scan current: answers from memory while the watcher says nothing has
/// moved, and rescans exactly once when it has, however many callers are waiting on it.
/// </summary>
public sealed class WorkingCopySession : IDisposable
{
    /// <summary>
    /// Past this many distinct paths, stop tracking them and rescan. Not a correctness bound —
    /// re-resolving more would still be right — but the tree walk amortises, and an unbounded set
    /// would let a long import grow one entry per file.
    /// </summary>
    private const int MaxIncrementalPaths = 1000;

    private readonly IWorkingCopyScan _scan;
    private readonly IIncrementalScan? _incremental;
    private readonly IUnfinishedWorkScan? _unfinishedWork;
    private readonly IUnrecordedMoveScan? _unrecordedMoves;
    private readonly IChangeNotifier _notifier;
    private readonly SemaphoreSlim _rescanning = new(1, 1);

    // Guards all three together: a reader must never see a generation without the path that
    // caused it, or it would store a scan as current that is already behind.
    private readonly Lock _pending = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.Ordinal);
    private bool _pendingIsUnusable;

    private long _generation;
    private Scanned? _scanned;

    /// <param name="incremental">
    /// <see langword="null"/> for a reader that can only answer about the whole working copy, which
    /// simply means every change costs a rescan — the behaviour before this existed.
    /// </param>
    /// <param name="unfinishedWork">
    /// <see langword="null"/> for a reader that cannot see an interrupted operation, which then
    /// reports none.
    /// </param>
    /// <param name="unrecordedMoves">
    /// <see langword="null"/> for a reader that cannot pair a rename made outside SVN, which then
    /// reports none.
    /// </param>
    public WorkingCopySession(
        IWorkingCopyScan scan,
        IChangeNotifier notifier,
        IIncrementalScan? incremental = null,
        IUnfinishedWorkScan? unfinishedWork = null,
        IUnrecordedMoveScan? unrecordedMoves = null
    )
    {
        _scan = scan;
        _incremental = incremental;
        _unfinishedWork = unfinishedWork;
        _unrecordedMoves = unrecordedMoves;
        _notifier = notifier;
        _notifier.Changed += OnChanged;
    }

    public WorkingCopyInfo Info => _scan.Info;

    /// <summary>Bumped by every change seen. The warmer watches it to tell "busy" from "settled".</summary>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>Nodes currently held, or zero before the first scan.</summary>
    public int EntryCount => Volatile.Read(ref _scanned)?.Entries.Count ?? 0;

    /// <summary>
    /// <see cref="WatcherState.Recovering"/> covers both halves of D5: the moment after a buffer
    /// overflow, and the ordinary case of a change seen but not yet scanned. Either way the next
    /// answer comes from a fresh scan, and saying so is better than a front-end guessing.
    /// </summary>
    public WatcherState WatcherState =>
        !_notifier.IsWatching ? WatcherState.Unavailable
        : Current() is null ? WatcherState.Recovering
        : WatcherState.Healthy;

    /// <summary>Whether a call to <see cref="CurrentAsync"/> would have to scan.</summary>
    public bool IsStale => Current() is null;

    /// <summary>
    /// Marks the held scan as no longer standing, so the next answer comes from a fresh one.
    /// </summary>
    /// <remarks>
    /// For writes Subverted makes itself. The watcher reports those too, but not necessarily before
    /// the next request arrives — and <c>sv add x &amp;&amp; sv st</c> has to show the add.
    /// </remarks>
    public void Invalidate() => OnChanged(WorkingCopyChange.Unknown);

    public async Task<CurrentScan> CurrentAsync(CancellationToken cancellationToken)
    {
        if (Current() is { } warm)
        {
            return Answer(warm, servedFromWarmIndex: true);
        }

        await _rescanning.WaitAsync(cancellationToken);
        try
        {
            if (Current() is { } scannedWhileWaiting)
            {
                return Answer(scannedWhileWaiting, servedFromWarmIndex: true);
            }

            // Taken *before* the work, and emptied at the same instant: a change arriving from
            // here on lands in the next batch and moves the generation past this one, so the
            // result is stale rather than wrong. One wasted pass, never a swallowed save.
            var (generation, changed, isUnusable) = TakePending();

            var entries = await UpdateOrScanAsync(changed, isUnusable, cancellationToken);

            // Under the same lock as the scan, because both read the one SQLite connection. Read
            // every time rather than once at open: a client can crash mid-operation while the
            // daemon holds this working copy, and `sv cleanup` clears it while it still does.
            var unfinished = _unfinishedWork?.CountUnfinishedOperations() ?? 0;

            // Under the same lock and for the same reason, and recomputed on the incremental path
            // too: that path can turn a node Missing, which is one half of a rename made outside
            // SVN — carrying the old answer forward would hide the pair exactly when it appears.
            var moves = _unrecordedMoves?.FindUnrecordedMoves(entries) ?? [];

            var scanned = new Scanned(entries, generation, unfinished, moves, Guid.NewGuid());
            Volatile.Write(ref _scanned, scanned);
            return Answer(scanned, servedFromWarmIndex: false);
        }
        finally
        {
            _rescanning.Release();
        }
    }

    private static CurrentScan Answer(Scanned scanned, bool servedFromWarmIndex) =>
        new(
            scanned.Entries,
            servedFromWarmIndex,
            scanned.UnfinishedOperations,
            scanned.UnrecordedMoves,
            scanned.Id
        );

    /// <summary>
    /// Re-resolves the changed paths over the held scan where that is possible, and otherwise
    /// reads the working copy again. Both answers are complete; only the cost differs.
    /// </summary>
    private async Task<IReadOnlyList<WorkingCopyEntry>> UpdateOrScanAsync(
        IReadOnlySet<string> changed,
        bool isUnusable,
        CancellationToken cancellationToken
    )
    {
        if (!isUnusable && _incremental is not null && Volatile.Read(ref _scanned) is { } held)
        {
            var updated = await _incremental.TryApplyAsync(
                held.Entries,
                changed,
                cancellationToken
            );
            if (updated is not null)
            {
                return updated;
            }
        }

        return await _scan.ScanAsync(cancellationToken);
    }

    /// <summary>
    /// The generation and the paths that reached it, read as one — and cleared, because they are
    /// about to be accounted for.
    /// </summary>
    private (long Generation, IReadOnlySet<string> Changed, bool IsUnusable) TakePending()
    {
        lock (_pending)
        {
            var taken = new HashSet<string>(_pendingPaths, StringComparer.Ordinal);
            var isUnusable = _pendingIsUnusable;

            _pendingPaths.Clear();
            _pendingIsUnusable = false;

            return (Interlocked.Read(ref _generation), taken, isUnusable);
        }
    }

    /// <returns>The held scan while it still stands, <c>null</c> when it has to be redone.</returns>
    private Scanned? Current()
    {
        var scanned = Volatile.Read(ref _scanned);
        return
            scanned is not null
            && scanned.Generation == Interlocked.Read(ref _generation)
            && _notifier.IsWatching
            ? scanned
            : null;
    }

    /// <remarks>
    /// The generation moves inside the lock, with the path. Bumping it outside would let a reader
    /// see the new number while the set that explains it is still empty — and conclude, correctly
    /// by its own lights, that nothing needed doing.
    /// </remarks>
    private void OnChanged(WorkingCopyChange change)
    {
        lock (_pending)
        {
            if (change.RelPath is { } relPath && _pendingPaths.Count < MaxIncrementalPaths)
            {
                _pendingPaths.Add(relPath);
            }
            else
            {
                _pendingIsUnusable = true;
            }

            Interlocked.Increment(ref _generation);
        }
    }

    public void Dispose()
    {
        _notifier.Changed -= OnChanged;
        _notifier.Dispose();
        _scan.Dispose();
        _rescanning.Dispose();
    }

    /// <param name="Id">
    /// Not the generation: a session that is not watching rescans at the same generation every
    /// time, and a daemon restarted counts from the start again. A fresh id per reading is neither.
    /// </param>
    private sealed record Scanned(
        IReadOnlyList<WorkingCopyEntry> Entries,
        long Generation,
        int UnfinishedOperations,
        IReadOnlyList<UnrecordedMove> UnrecordedMoves,
        Guid Id
    );
}
