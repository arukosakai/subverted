using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class ChangeSummaryTests
{
    [Test]
    public async Task A_clean_listing_summarises_to_nothing()
    {
        await Assert.That(ChangeSummary.Of([])).IsEmpty();
    }

    /// <summary>Most urgent first: a conflict blocks a commit, an unversioned file never does.</summary>
    [Test]
    public async Task Counts_are_ordered_by_what_needs_a_person_first()
    {
        var summary = ChangeSummary.Of(
            Rows(
                Entry("a", NodeStatus.Unversioned),
                Entry("b", NodeStatus.Modified),
                Entry("c", NodeStatus.Conflicted),
                Entry("d", NodeStatus.Deleted),
                Entry("e", NodeStatus.Added),
                Entry("f", NodeStatus.Missing),
                Entry("g", NodeStatus.Replaced)
            )
        );

        await Assert
            .That(string.Join(",", summary.Select(count => count.Tone)))
            .IsEqualTo("Conflict,Missing,Modified,Added,Deleted,Replaced,Unversioned");
    }

    [Test]
    [Arguments(NodeStatus.Modified, 2, "2 modified")]
    [Arguments(NodeStatus.Added, 3, "3 added")]
    [Arguments(NodeStatus.Deleted, 1, "1 deleted")]
    [Arguments(NodeStatus.Replaced, 1, "1 replaced")]
    [Arguments(NodeStatus.Missing, 2, "2 missing")]
    [Arguments(NodeStatus.Unversioned, 4, "4 not versioned")]
    [Arguments(NodeStatus.Conflicted, 1, "1 conflict")]
    [Arguments(NodeStatus.Conflicted, 2, "2 conflicts")]
    public async Task Each_count_reads_as_a_phrase(NodeStatus status, int count, string text)
    {
        var rows = Rows([
            .. Enumerable.Range(0, count).Select(index => Entry($"f{index}", status)),
        ]);

        await Assert.That(ChangeSummary.Of(rows).Single().Text).IsEqualTo(text);
    }

    [Test]
    public async Task Renames_are_counted_after_replacements_and_before_unversioned_files()
    {
        var summary = ChangeSummary.Of([
            ChangeRow.From(Entry("u", NodeStatus.Unversioned)),
            ChangeRow.Rename(Entry("n1", NodeStatus.Unversioned), "o1"),
            ChangeRow.Rename(Entry("n2", NodeStatus.Unversioned), "o2"),
            ChangeRow.From(Entry("r", NodeStatus.Replaced)),
        ]);

        await Assert
            .That(string.Join(",", summary.Select(count => count.Text)))
            .IsEqualTo("1 replaced,2 renamed,1 not versioned");
    }

    /// <summary>An ignored or merely locked node is listed but is no change, so it is not counted.</summary>
    [Test]
    public async Task Quiet_rows_are_listed_but_not_counted()
    {
        var summary = ChangeSummary.Of(
            Rows(
                Entry("a", NodeStatus.Ignored),
                Entry("b", NodeStatus.Unmodified, hasLockToken: true)
            )
        );

        await Assert.That(summary).IsEmpty();
    }

    private static IReadOnlyList<ChangeRow> Rows(params WorkingCopyEntry[] entries) =>
        [.. entries.Select(ChangeRow.From)];
}
