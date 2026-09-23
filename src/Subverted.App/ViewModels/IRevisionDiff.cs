using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>What the History diff pane needs from the daemon: one revision's change to one path.</summary>
public interface IRevisionDiff
{
    /// <param name="workingCopyPath">Absolute, inside the working copy; it says which repository.</param>
    /// <param name="repositoryPath">Repository-absolute, as the revision listed it.</param>
    /// <returns>A <see cref="DiffResponse"/>, or an <see cref="ErrorResponse"/> saying why not.</returns>
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    Task<DaemonResponse> ReadAsync(
        string workingCopyPath,
        string repositoryPath,
        long revision,
        CancellationToken cancellationToken
    );
}
