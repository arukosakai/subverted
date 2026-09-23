using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What the History list needs from the daemon: pages of history, and where BASE is.</summary>
public interface IRevisionHistory
{
    /// <param name="path">Absolute, inside a working copy; the history is that path's.</param>
    /// <param name="start">The newest revision to list.</param>
    /// <param name="limit">How many revisions at most.</param>
    /// <returns>A <see cref="LogResponse"/>, or an <see cref="ErrorResponse"/> saying why not.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> ReadAsync(
        string path,
        HistoryStart start,
        int limit,
        CancellationToken cancellationToken
    );

    /// <returns>A <see cref="WorkingCopyRevisionResponse"/>, or an <see cref="ErrorResponse"/>.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> ReadBaseRangeAsync(string path, CancellationToken cancellationToken);
}
