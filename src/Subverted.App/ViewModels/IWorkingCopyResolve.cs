using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What the resolve menu needs from the daemon: settle the conflicts under a path.</summary>
public interface IWorkingCopyResolve
{
    /// <param name="path">Absolute; resolved to infinite depth. Already confirmed if it had to be.</param>
    /// <returns>A <see cref="ResolveResponse"/>, or an <see cref="ErrorResponse"/> saying why not.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> ResolveAsync(
        string path,
        ConflictResolution resolution,
        CancellationToken cancellationToken
    );
}
