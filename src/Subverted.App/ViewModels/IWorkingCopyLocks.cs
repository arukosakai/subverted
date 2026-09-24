using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What the lock menu needs from the daemon: take a file's lock, and give it back.</summary>
public interface IWorkingCopyLocks
{
    /// <param name="path">Absolute; a file. A lock somebody else holds is left with them.</param>
    /// <returns>A <see cref="LockResponse"/>, or an <see cref="ErrorResponse"/> saying why not.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> LockAsync(string path, CancellationToken cancellationToken);

    /// <param name="path">Absolute; a file this working copy holds the lock on.</param>
    /// <returns>An <see cref="UnlockResponse"/>, or an <see cref="ErrorResponse"/> saying why not.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> UnlockAsync(string path, CancellationToken cancellationToken);
}
