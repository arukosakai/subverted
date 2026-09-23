namespace Subverted.Daemon;

/// <summary>
/// Reads what one committed revision did to one repository path, as a unified diff. Declared here
/// for the same reason as <see cref="ReadRevisionLog"/>.
/// </summary>
/// <param name="repositoryRoot">The working copy's repository root URL.</param>
/// <param name="repositoryPath">Repository-absolute, with a leading <c>/</c>.</param>
public delegate Task<string> ReadRevisionDiff(
    string workingCopyRoot,
    string repositoryRoot,
    string repositoryPath,
    long revision,
    CancellationToken cancellationToken
);
