namespace Subverted.Core;

/// <param name="Author">
/// Null for a revision committed without authentication, or one whose <c>svn:author</c> revision
/// property has been removed.
/// </param>
/// <param name="Date">Null when <c>svn:date</c> is absent or unreadable.</param>
/// <param name="Message">Empty rather than null: SVN accepts a commit with no message at all.</param>
/// <param name="ChangedPaths">Empty when the history was read without asking for paths.</param>
public sealed record RevisionEntry(
    long Revision,
    string? Author,
    DateTimeOffset? Date,
    string Message,
    IReadOnlyList<ChangedPath> ChangedPaths
);
