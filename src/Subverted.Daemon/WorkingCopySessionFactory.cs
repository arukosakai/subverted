using Microsoft.Extensions.Logging;
using Subverted.Svn;

namespace Subverted.Daemon;

/// <summary>
/// Opens a working copy for the daemon to hold: the read of its metadata, a watch over its root,
/// and the session that keeps the two in step.
/// </summary>
/// <param name="globalIgnorePatterns">
/// Read once at startup from this machine's SVN runtime configuration, because it is the same for
/// every working copy and reading it per scan would be wasted I/O.
/// </param>
public sealed class WorkingCopySessionFactory(
    IReadOnlyList<string> globalIgnorePatterns,
    ReadWorkingCopyInfo readWorkingCopyInfo,
    ReadWorkingCopyStatus readWorkingCopyStatus,
    ILogger<WorkingCopySessionFactory> logger
)
{
    /// <summary>
    /// Reads wc.db directly where it can and through the <c>svn</c> client where it cannot. A
    /// database this build does not understand is the case the fallback exists for: the studio
    /// upgrading Subversion must not be the day <c>sv st</c> stops answering (D14).
    /// </summary>
    /// <exception cref="WcDbException">There is no working copy at or above the path.</exception>
    /// <exception cref="SvnCommandException">
    /// wc.db was unreadable and the client could not answer for it either.
    /// </exception>
    public async Task<WorkingCopySession> OpenAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return Watching(new SvnWorkingCopyScan(WcDbReader.Open(path), globalIgnorePatterns));
        }
        catch (WcDbException exception) when (exception.Failure == WcDbFailure.Unreadable)
        {
            logger.LogWarning(
                "Reading {Path} through the svn client instead of wc.db: {Reason}",
                path,
                exception.Message
            );

            return Watching(
                new SvnCliWorkingCopyScan(
                    await readWorkingCopyInfo(path, cancellationToken),
                    readWorkingCopyStatus
                )
            );
        }
    }

    /// <summary>
    /// Pairs a scan with a watch over the root it reports. A watch that cannot be set up is a
    /// warning, not a failure — the session then rescans every time. The catch is for the one
    /// escape <see cref="FileSystemChangeNotifier"/> does not absorb, a platform with no watcher at
    /// all: without it that would leak an open wc.db for the life of the daemon.
    /// </summary>
    private WorkingCopySession Watching(IWorkingCopyScan scan)
    {
        try
        {
            var notifier = new FileSystemChangeNotifier(scan.Info.RootPath);
            if (notifier.UnavailableReason is { } reason)
            {
                logger.LogWarning(
                    "Not watching {Root}: {Reason}. Every status request will rescan.",
                    scan.Info.RootPath,
                    reason
                );
            }

            // The wc.db reader can re-resolve one path; the CLI fallback would need a process per
            // path, so it gets null and every change costs it a rescan. It gets null for the work
            // queue too, for a different reason: `svn status` is what fails on a copy that has one.
            // And null for unrecorded moves, because pairing them is against the checksum wc.db
            // recorded and the client's status never reports one.
            return new WorkingCopySession(
                scan,
                notifier,
                scan as IIncrementalScan,
                scan as IUnfinishedWorkScan,
                scan as IUnrecordedMoveScan
            );
        }
        catch
        {
            scan.Dispose();
            throw;
        }
    }
}
