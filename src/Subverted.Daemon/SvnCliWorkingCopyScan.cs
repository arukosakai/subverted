using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// The same working copy read through the <c>svn</c> client instead of wc.db — D1's fallback, held
/// by a session exactly like the fast path so that everything above it is unchanged. There is
/// nothing to dispose: the client is a process per scan, not a handle kept open.
/// </summary>
internal sealed class SvnCliWorkingCopyScan(WorkingCopyInfo info, ReadWorkingCopyStatus readStatus)
    : IWorkingCopyScan
{
    public WorkingCopyInfo Info => info;

    public Task<IReadOnlyList<WorkingCopyEntry>> ScanAsync(CancellationToken cancellationToken) =>
        readStatus(info.RootPath, cancellationToken);

    public void Dispose() { }
}
