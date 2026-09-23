using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What the commit box needs from the daemon: send exactly these changes.</summary>
public interface IWorkingCopyCommit
{
    /// <param name="paths">Absolute, all in one working copy; both halves of every rename.</param>
    /// <param name="message">The log message, never blank.</param>
    /// <returns>
    /// A <see cref="CommitSelectionResponse"/>, a <see cref="SelectionNotCommittedResponse"/> when
    /// marks were made and a later step failed, or an <see cref="ErrorResponse"/> when nothing was
    /// written.
    /// </returns>
    /// <exception cref="DaemonUnreachableException">
    /// No daemon answered, or it went away mid-answer — in which case the commit may have happened.
    /// </exception>
    Task<DaemonResponse> CommitAsync(
        IReadOnlyList<string> paths,
        string message,
        CancellationToken cancellationToken
    );
}
