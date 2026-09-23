using Subverted.App.Presentation;
using Subverted.Core;

namespace Subverted.App.Tests;

public sealed class HistoryCursorTests
{
    private const int PageSize = 3;

    [Test]
    public async Task The_first_page_starts_at_head()
    {
        await Assert.That(HistoryCursor.Fresh.Next()).IsEqualTo(new HistoryFromHead());
    }

    [Test]
    public async Task A_full_page_is_followed_by_one_starting_below_its_oldest_revision()
    {
        var cursor = HistoryCursor.Fresh.After([60, 42, 17], PageSize);

        await Assert.That(cursor.HasMore).IsTrue();
        await Assert.That(cursor.Oldest).IsEqualTo(17L);
        await Assert.That(cursor.Next()).IsEqualTo(new HistoryFromRevision(16));
    }

    [Test]
    public async Task A_page_one_short_of_the_limit_is_the_end_of_the_history()
    {
        var cursor = HistoryCursor.Fresh.After([60, 42], PageSize);

        await Assert.That(cursor.HasMore).IsFalse();
        await Assert.That(cursor.Oldest).IsEqualTo(42L);
    }

    /// <summary>Nothing is older than revision 1, so asking again would be a round trip for nothing.</summary>
    [Test]
    public async Task A_full_page_reaching_revision_one_is_the_end_too()
    {
        await Assert.That(HistoryCursor.Fresh.After([3, 2, 1], PageSize).HasMore).IsFalse();
    }

    [Test]
    public async Task A_full_page_stopping_at_revision_two_may_still_have_revision_one_below_it()
    {
        await Assert.That(HistoryCursor.Fresh.After([4, 3, 2], PageSize).HasMore).IsTrue();
    }

    [Test]
    public async Task An_empty_page_ends_the_history_and_keeps_the_oldest_already_read()
    {
        var cursor = HistoryCursor.Fresh.After([9, 8, 7], PageSize).After([], PageSize);

        await Assert.That(cursor.HasMore).IsFalse();
        await Assert.That(cursor.Oldest).IsEqualTo(7L);
    }

    [Test]
    public async Task There_is_no_next_page_once_the_history_has_ended()
    {
        var cursor = HistoryCursor.Fresh.After([5], PageSize);

        await Assert.That(() => cursor.Next()).Throws<InvalidOperationException>();
    }
}
