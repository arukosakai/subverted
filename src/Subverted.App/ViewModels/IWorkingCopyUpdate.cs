using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What the Update button needs from the daemon: bring a path up to the repository's latest.</summary>
public interface IWorkingCopyUpdate
{
    /// <param name="path">Absolute; updated to infinite depth.</param>
    /// <returns>An <see cref="UpdateResponse"/>, or an <see cref="ErrorResponse"/> saying why not.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> UpdateAsync(string path, CancellationToken cancellationToken);
}
