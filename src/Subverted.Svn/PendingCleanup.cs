namespace Subverted.Svn;

/// <summary>
/// The two things a working copy can be stuck on, read straight out of wc.db. Both are what
/// <c>svn cleanup</c> exists to clear, and they behave differently enough that counting them
/// together would lose the distinction.
/// </summary>
/// <param name="WriteLocks">
/// Directories a client holds, or died holding. Every write to the working copy fails with
/// <c>E155004</c> while one stands, but <c>svn status</c> still answers — it prints <c>L</c>.
/// </param>
/// <param name="UnfinishedOperations">
/// Queued steps of an interrupted operation. Unlike a write lock these stop <c>svn status</c>
/// itself, which fails with <c>E155037</c> and reads nothing.
/// </param>
public sealed record PendingCleanup(
    IReadOnlyList<WorkingCopyWriteLock> WriteLocks,
    int UnfinishedOperations
)
{
    public static readonly PendingCleanup Nothing = new([], 0);

    /// <summary>Reads the current state of the working copy rooted at <paramref name="root"/>.</summary>
    /// <exception cref="WcDbException">Not a working copy, or its schema is not understood.</exception>
    public static PendingCleanup Read(string root)
    {
        using var reader = WcDbReader.Open(root);
        return new PendingCleanup(reader.ReadWriteLocks(), reader.CountUnfinishedWork());
    }
}
