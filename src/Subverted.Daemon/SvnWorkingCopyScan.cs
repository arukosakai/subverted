using Subverted.Core;
using Subverted.Svn;

namespace Subverted.Daemon;

/// <summary>
/// The SVN scanner behind the two members a session needs. The daemon owns the reader's lifetime,
/// so disposing the scan closes wc.db.
/// </summary>
internal sealed class SvnWorkingCopyScan(
    WcDbReader reader,
    IReadOnlyList<string> globalIgnorePatterns
) : IWorkingCopyScan, IIncrementalScan, IUnfinishedWorkScan, IUnrecordedMoveScan
{
    private readonly WorkingCopyScanner _scanner = new(reader, globalIgnorePatterns);

    public WorkingCopyInfo Info => _scanner.Info;

    /// <remarks>
    /// The fast path is synchronous work — filesystem metadata and hashing, already spread across
    /// every core inside the scanner — so it runs on the calling thread rather than being handed to
    /// the pool only to be waited on.
    /// </remarks>
    public Task<IReadOnlyList<WorkingCopyEntry>> ScanAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_scanner.Scan());

    /// <inheritdoc />
    /// <remarks>
    /// Synchronous for the same reason as <see cref="ScanAsync"/>, and more so: the whole point is
    /// that this is a handful of stats rather than a tree walk.
    /// </remarks>
    public Task<IReadOnlyList<WorkingCopyEntry>?> TryApplyAsync(
        IReadOnlyList<WorkingCopyEntry> held,
        IReadOnlySet<string> changedRelPaths,
        CancellationToken cancellationToken
    ) => Task.FromResult(_scanner.TryApplyChanges(held, changedRelPaths));

    /// <inheritdoc />
    public int CountUnfinishedOperations() => _scanner.CountUnfinishedOperations();

    /// <inheritdoc />
    public IReadOnlyList<UnrecordedMove> FindUnrecordedMoves(
        IReadOnlyList<WorkingCopyEntry> entries
    ) => _scanner.FindUnrecordedMoves(entries);

    public void Dispose() => reader.Dispose();
}
