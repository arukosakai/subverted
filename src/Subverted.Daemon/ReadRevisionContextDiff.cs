using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Reads what one revision did to one repository path with more context than <c>svn diff -c</c>
/// prints. Declared here for the same reason as <see cref="ReadRevisionLog"/>.
/// </summary>
/// <returns>
/// The diff, or <see langword="null"/> when this path's diff can only come from
/// <see cref="ReadRevisionDiff"/>, at its three lines.
/// </returns>
public delegate Task<string?> ReadRevisionContextDiff(
    string workingCopyRoot,
    string repositoryRoot,
    string repositoryPath,
    long revision,
    DiffContext context,
    CancellationToken cancellationToken
);
