using Subverted.Core;

namespace Subverted.Daemon.Tests;

/// <summary>
/// A scan that counts how often it was asked to run and can be held open mid-scan. Counting is the
/// whole point: the session's contract is "scan once per change", and only a counter tells a cache
/// that works from one that quietly rescans every time.
/// </summary>
internal sealed class FakeWorkingCopyScan(string rootPath = "/wc") : IWorkingCopyScan
{
    public int Scans { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Runs inside <see cref="ScanAsync"/>, so a test can change the world mid-scan.</summary>
    public Action? DuringScan { get; set; }

    public IReadOnlyList<WorkingCopyEntry> Entries { get; set; } = [];

    public WorkingCopyInfo Info { get; } = new(rootPath, "https://svn.example/repo", "uuid-1", 31);

    public Task<IReadOnlyList<WorkingCopyEntry>> ScanAsync(CancellationToken cancellationToken)
    {
        Scans++;
        DuringScan?.Invoke();
        return Task.FromResult(Entries);
    }

    public void Dispose() => IsDisposed = true;
}
