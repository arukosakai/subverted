using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What the revert prompt needs from the daemon: throw away the local changes under a path.</summary>
public interface IWorkingCopyRevert
{
    /// <param name="path">Absolute; reverted to infinite depth. Already confirmed by the person.</param>
    /// <returns>A <see cref="RevertResponse"/>, or an <see cref="ErrorResponse"/> saying why not.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> RevertAsync(string path, CancellationToken cancellationToken);
}
