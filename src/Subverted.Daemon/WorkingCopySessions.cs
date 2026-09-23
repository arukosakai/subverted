namespace Subverted.Daemon;

/// <summary>
/// The working copies the daemon is holding. Finds the one serving a path, opening it the first
/// time anyone asks about it and keeping it for the life of the process.
/// </summary>
/// <param name="open">
/// Opens the working copy containing a path. Throws whatever the SVN layer throws when there is
/// none, which the caller turns into an answer.
/// </param>
public sealed class WorkingCopySessions(
    Func<string, CancellationToken, Task<WorkingCopySession>> open
) : IDisposable
{
    private readonly Dictionary<string, WorkingCopySession> _byRoot = new(
        StringComparer.FromComparison(ContainingRoot.PlatformComparison)
    );
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _opening = new(1, 1);

    public IReadOnlyList<WorkingCopySession> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _byRoot.Values];
            }
        }
    }

    public async Task<WorkingCopySession> ForAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (Find(fullPath) is { } held)
        {
            return held;
        }

        await _opening.WaitAsync(cancellationToken);
        try
        {
            if (Find(fullPath) is { } openedWhileWaiting)
            {
                return openedWhileWaiting;
            }

            var session = await open(fullPath, cancellationToken);
            var root = Path.GetFullPath(session.Info.RootPath);

            lock (_gate)
            {
                // The lookup above misses when the root normalises to something the request path
                // does not sit under. Keeping the first session and closing this one means a
                // second wc.db handle never outlives the request that opened it.
                if (_byRoot.TryGetValue(root, out var alreadyHeld))
                {
                    session.Dispose();
                    return alreadyHeld;
                }

                _byRoot.Add(root, session);
                return session;
            }
        }
        finally
        {
            _opening.Release();
        }
    }

    private WorkingCopySession? Find(string fullPath)
    {
        lock (_gate)
        {
            var root = ContainingRoot.Of(fullPath, _byRoot.Keys, ContainingRoot.PlatformComparison);
            return root is null ? null : _byRoot[root];
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var session in _byRoot.Values)
            {
                session.Dispose();
            }

            _byRoot.Clear();
        }

        _opening.Dispose();
    }
}
