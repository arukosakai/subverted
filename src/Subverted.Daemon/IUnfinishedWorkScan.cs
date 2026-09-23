namespace Subverted.Daemon;

/// <summary>
/// Whether a working copy has queued steps of an operation that was interrupted — the state
/// <c>svn cleanup</c> finishes. Separate from <see cref="IWorkingCopyScan"/> because only the
/// wc.db reader can answer it: a <c>WORK_QUEUE</c> row makes <c>svn status</c> fail outright with
/// <c>E155037</c>, so the CLI fallback reports this state by failing rather than by counting.
/// </summary>
public interface IUnfinishedWorkScan
{
    /// <returns>Queued steps of interrupted operations; zero on a working copy nothing is mid-way through.</returns>
    /// <remarks>
    /// Synchronous, and called only while the session holds its scanning lock: this shares one
    /// SQLite connection with the scan itself, and no second thread may touch it.
    /// </remarks>
    int CountUnfinishedOperations();
}
