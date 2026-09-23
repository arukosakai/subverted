namespace Subverted.Core;

/// <param name="Revision">
/// The revision the server created, or <see langword="null"/> when nothing was committed — SVN
/// treats "no local changes under these paths" as success and says nothing at all.
/// </param>
/// <param name="Notifications">
/// SVN's own per-path notification text, unparsed. Reproducing its wording would only produce a
/// second answer to disagree with.
/// </param>
public sealed record CommitOutcome(long? Revision, string Notifications);
