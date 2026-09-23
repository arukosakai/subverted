namespace Subverted.Core;

/// <param name="Format">
/// wc.db schema format (SQLite <c>user_version</c>), e.g. 31 for SVN 1.8+. Null when the working
/// copy was read through the <c>svn</c> client rather than wc.db — the client does not report it,
/// and inventing a number would defeat the version gate that made the fallback necessary.
/// </param>
public sealed record WorkingCopyInfo(
    string RootPath,
    string RepositoryRoot,
    string RepositoryUuid,
    int? Format
);
