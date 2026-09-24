using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// What the delete prompt needs from the daemon: to see everything a delete would reach, and then
/// to delete it.
/// </summary>
public interface IWorkingCopyDeletion
{
    /// <param name="path">Absolute: the node about to be deleted.</param>
    /// <returns>
    /// A <see cref="StatusResponse"/> of the node and everything beneath it, unmodified and ignored
    /// nodes included; or an <see cref="ErrorResponse"/> saying why not.
    /// </returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> ListAsync(string path, CancellationToken cancellationToken);

    /// <param name="path">Absolute; deleted from disk with everything in it. Already confirmed by the person.</param>
    /// <returns>A <see cref="DeleteResponse"/>, or an <see cref="ErrorResponse"/> saying why not.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> DeleteAsync(string path, CancellationToken cancellationToken);
}
