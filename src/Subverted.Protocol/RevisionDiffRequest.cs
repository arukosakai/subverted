using Subverted.Core;

namespace Subverted.Protocol;

/// <summary>
/// What one committed revision did to one path, answered with a <see cref="DiffResponse"/>. The
/// path is named in the repository rather than on disk because a revision's paths often no longer
/// exist there — deleted since, renamed since, or in a part of the repository not checked out.
/// </summary>
/// <param name="WorkingCopyPath">Any absolute path inside the working copy; it says which repository.</param>
/// <param name="RepositoryPath">
/// Repository-absolute with a leading <c>/</c>, as a <c>ChangedPath</c> spells it. SVN prints the
/// diff's headers relative to it, so a file's header is its bare name.
/// </param>
/// <param name="Revision">The revision, at least 1.</param>
/// <param name="Context">As <see cref="DiffRequest.Context"/>: null is <c>svn diff -c</c>'s own three lines.</param>
public sealed record RevisionDiffRequest(
    string WorkingCopyPath,
    string RepositoryPath,
    long Revision,
    DiffContext? Context = null
) : DaemonRequest;
