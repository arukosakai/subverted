using Subverted.App.Presentation;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What the working-copy view needs from the daemon: the current listing, and nothing else.</summary>
public interface IWorkingCopyStatus
{
    /// <param name="heldScan">
    /// The <see cref="StatusResponse.ScanId"/> of the listing already on screen for the same
    /// <paramref name="listed"/>; null to be sent the entries whatever they are.
    /// </param>
    /// <returns>
    /// A <see cref="StatusResponse"/>; a <see cref="StatusUnchangedResponse"/> when
    /// <paramref name="heldScan"/> is still current; or an <see cref="ErrorResponse"/> saying why not.
    /// </returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> ReadAsync(
        string workingCopyPath,
        ListedNodes listed,
        Guid? heldScan,
        CancellationToken cancellationToken
    );
}
