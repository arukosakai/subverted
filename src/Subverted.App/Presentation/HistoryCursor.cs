using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>
/// How far down the history the list has read, and where the next page starts. Pages are
/// newest first, so the next one begins one below the oldest revision already shown.
/// </summary>
/// <param name="Oldest">The oldest revision loaded so far, or null before the first page.</param>
/// <param name="HasMore">Whether asking again could list anything not already shown.</param>
public sealed record HistoryCursor(long? Oldest, bool HasMore)
{
    /// <summary>Nothing read yet; the first page starts at HEAD.</summary>
    public static readonly HistoryCursor Fresh = new(Oldest: null, HasMore: true);

    /// <summary>Where the next page starts.</summary>
    /// <exception cref="InvalidOperationException">There is nothing more to read.</exception>
    public HistoryStart Next() =>
        (HasMore, Oldest) switch
        {
            (false, _) => throw new InvalidOperationException("The whole history is loaded."),
            (true, null) => new HistoryFromHead(),
            (true, { } oldest) => new HistoryFromRevision(oldest - 1),
        };

    /// <summary>The cursor once a page has come back.</summary>
    /// <param name="page">The page's revisions, newest first, as SVN lists them.</param>
    /// <param name="pageSize">The limit the page was asked for with.</param>
    /// <remarks>
    /// A short page is the end; so is a page reaching revision 1, even a full one, since nothing
    /// is older — which saves the empty round trip that would otherwise prove it.
    /// </remarks>
    public HistoryCursor After(IReadOnlyList<long> page, int pageSize)
    {
        if (page.Count == 0)
        {
            return this with { HasMore = false };
        }

        var oldest = page[^1];
        return new HistoryCursor(oldest, HasMore: page.Count >= pageSize && oldest > 1);
    }
}
