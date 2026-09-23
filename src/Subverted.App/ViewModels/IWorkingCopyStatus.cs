using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What the working-copy view needs from the daemon: the current listing, and nothing else.</summary>
public interface IWorkingCopyStatus
{
    /// <returns>A <see cref="StatusResponse"/>, or an <see cref="ErrorResponse"/> saying why not.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> ReadAsync(string workingCopyPath, CancellationToken cancellationToken);
}
