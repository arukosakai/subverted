using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// One full read of a working copy. This is the whole of the SVN layer a session needs, and naming
/// it here rather than depending on the scanner is what lets the session be tested without a
/// working copy on disk — and what lets the same session serve wc.db and the <c>svn</c> client.
/// </summary>
public interface IWorkingCopyScan : IDisposable
{
    WorkingCopyInfo Info { get; }

    /// <summary>
    /// Reads the working copy as it is right now. Expensive; the session decides when. Asynchronous
    /// because the fallback is a child process, not a database read.
    /// </summary>
    Task<IReadOnlyList<WorkingCopyEntry>> ScanAsync(CancellationToken cancellationToken);
}
