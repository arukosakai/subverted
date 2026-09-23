using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Reads which working copy a path belongs to through the <c>svn</c> client. Declared here rather
/// than taken as an SVN type so the fallback can be tested without a repository or <c>svn</c> on
/// PATH.
/// </summary>
public delegate Task<WorkingCopyInfo> ReadWorkingCopyInfo(
    string path,
    CancellationToken cancellationToken
);
