using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What the diff pane needs from the daemon: one path's local changes.</summary>
public interface IWorkingCopyDiff
{
    /// <param name="path">Absolute; the diff covers it and everything below it.</param>
    /// <returns>A <see cref="DiffResponse"/>, or an <see cref="ErrorResponse"/> saying why not.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> ReadAsync(string path, CancellationToken cancellationToken);
}
