using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Updating a held scan in place, for readers that can answer about one path without reading the
/// whole working copy. Separate from <see cref="IWorkingCopyScan"/> because not every reader can:
/// the CLI fallback would need a child process per path, which is worse than rescanning.
/// </summary>
public interface IIncrementalScan
{
    /// <returns>
    /// The updated entries, or <see langword="null"/> when this change cannot be applied without a
    /// full scan. Returning null is expected and safe; the session just rescans.
    /// </returns>
    Task<IReadOnlyList<WorkingCopyEntry>?> TryApplyAsync(
        IReadOnlyList<WorkingCopyEntry> held,
        IReadOnlySet<string> changedRelPaths,
        CancellationToken cancellationToken
    );
}
