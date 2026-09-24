using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class RevisionListItemTests
{
    private static readonly RevisionRow Row = new(
        12,
        "rena",
        "2026-09-24 19:02",
        "Faster player",
        "Faster player",
        []
    );

    [Test]
    public async Task A_screen_reader_hears_the_revision_summary_author_and_time()
    {
        var item = new RevisionListItem(Row, RevisionPresence.InCopy, null);

        await Assert
            .That(item.AutomationName)
            .IsEqualTo("r12, Faster player, by rena, 2026-09-24 19:02");
    }

    [Test]
    public async Task A_revision_with_no_recorded_time_says_none()
    {
        var item = new RevisionListItem(Row with { When = "" }, null, null);

        await Assert.That(item.AutomationName).IsEqualTo("r12, Faster player, by rena");
    }

    [Test]
    public async Task The_base_marker_drawn_above_the_row_is_said_first()
    {
        var item = new RevisionListItem(Row, RevisionPresence.InCopy, "Your copy is at r12");

        await Assert
            .That(item.AutomationName)
            .IsEqualTo("Your copy is at r12. r12, Faster player, by rena, 2026-09-24 19:02");
    }

    [Test]
    [Arguments(RevisionPresence.NotInCopy, ", not in your copy")]
    [Arguments(RevisionPresence.PartlyInCopy, ", in part of your copy")]
    [Arguments(RevisionPresence.InCopy, "")]
    public async Task The_presence_tag_is_said_as_it_reads(RevisionPresence presence, string tag)
    {
        var item = new RevisionListItem(Row, presence, null);

        await Assert
            .That(item.AutomationName)
            .IsEqualTo("r12, Faster player, by rena, 2026-09-24 19:02" + tag);
    }
}
