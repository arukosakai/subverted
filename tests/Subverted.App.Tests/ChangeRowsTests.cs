using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class ChangeRowsTests
{
    [Test]
    public async Task Without_renames_there_is_one_row_per_entry_in_the_listing_s_order()
    {
        var rows = ChangeRows.From([Entry("b.png"), Entry("a.png", NodeStatus.Missing)], []);

        await Assert.That(Describe(rows)).IsEqualTo("b.png:Modified,a.png:Missing");
    }

    [Test]
    public async Task A_paired_rename_is_one_row_at_the_new_path_carrying_the_old_one()
    {
        var rows = ChangeRows.From(
            [
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("a.txt"),
                Entry("art/protagonist.png", NodeStatus.Unversioned),
            ],
            [new UnrecordedMove("art/hero.png", "art/protagonist.png")]
        );

        await Assert.That(Describe(rows)).IsEqualTo("a.txt:Modified,art/protagonist.png:Renamed");
        await Assert.That(rows[1].RenamedFrom).IsEqualTo("art/hero.png");
        await Assert.That(rows[0].RenamedFrom).IsNull();
    }

    /// <summary>A status scoped to one folder can hold one half; alone it is what SVN sees.</summary>
    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task A_pair_with_a_half_missing_from_the_listing_stays_as_it_is(
        bool fromListed,
        bool toListed
    )
    {
        List<WorkingCopyEntry> entries = [];
        if (fromListed)
        {
            entries.Add(Entry("old.png", NodeStatus.Missing));
        }

        if (toListed)
        {
            entries.Add(Entry("new.png", NodeStatus.Unversioned));
        }

        var rows = ChangeRows.From(entries, [new UnrecordedMove("old.png", "new.png")]);

        await Assert.That(rows.All(row => row.RenamedFrom is null)).IsTrue();
        await Assert.That(rows.Count).IsEqualTo(1);
    }

    /// <summary>D27 never pairs one path twice; if it did, the first pair shows and nothing is lost.</summary>
    [Test]
    public async Task A_destination_paired_twice_shows_the_first_pair_and_keeps_the_other_half()
    {
        var rows = ChangeRows.From(
            [
                Entry("a.png", NodeStatus.Missing),
                Entry("b.png", NodeStatus.Missing),
                Entry("n.png", NodeStatus.Unversioned),
            ],
            [new UnrecordedMove("a.png", "n.png"), new UnrecordedMove("b.png", "n.png")]
        );

        await Assert.That(Describe(rows)).IsEqualTo("b.png:Missing,n.png:Renamed");
        await Assert.That(rows[1].RenamedFrom).IsEqualTo("a.png");
    }

    private static string Describe(IEnumerable<ChangeRow> rows) =>
        string.Join(",", rows.Select(row => $"{row.RelPath}:{row.Badge.Label}"));
}
