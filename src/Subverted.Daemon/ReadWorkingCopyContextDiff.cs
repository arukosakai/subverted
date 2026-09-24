using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Reads a file's local changes with more context than <c>svn diff</c> prints. Declared here for
/// the same reason as <see cref="ReadRevisionLog"/>.
/// </summary>
/// <returns>
/// The diff, or <see langword="null"/> when this path's diff can only come from
/// <see cref="ReadWorkingCopyDiff"/>, at its three lines.
/// </returns>
public delegate Task<string?> ReadWorkingCopyContextDiff(
    string workingCopyRoot,
    string path,
    DiffContext context,
    CancellationToken cancellationToken
);
