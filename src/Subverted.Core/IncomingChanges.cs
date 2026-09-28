namespace Subverted.Core;

/// <summary>What an update would bring down, as the server answered at one moment.</summary>
/// <param name="AgainstRevision">The server's latest revision when it answered — what an update would go to.</param>
/// <param name="Changes">
/// Every path SVN marked, in the order it listed them. A folder whose list of children changed is
/// marked as well as the children themselves; <c>IncomingCount</c> in Frontend is what leaves it out.
/// </param>
public sealed record IncomingChanges(long AgainstRevision, IReadOnlyList<IncomingChange> Changes);
