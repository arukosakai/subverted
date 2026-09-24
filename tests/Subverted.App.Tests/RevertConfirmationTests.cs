using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class RevertConfirmationTests
{
    [Test]
    public async Task A_file_lists_only_itself()
    {
        var rows = Rows(Entry("art/a.png"), Entry("art/b.png"));

        var confirmation = RevertConfirmation.For(rows[0], rows);

        await Assert.That(confirmation.Target).IsEqualTo("art/a.png");
        await Assert.That(Paths(confirmation)).IsEqualTo("art/a.png");
        await Assert.That(confirmation.Title).IsEqualTo("Revert art/a.png?");
        await Assert
            .That(confirmation.Warning)
            .IsEqualTo("What it throws away cannot be brought back.");
    }

    /// <summary>Revert goes to infinite depth, so a folder's list is everything the listing has beneath it.</summary>
    [Test]
    public async Task A_folder_lists_everything_beneath_it_in_path_order_and_not_its_prefix_siblings()
    {
        var rows = Rows(
            Entry("art/z.png", NodeStatus.Missing),
            Entry("art", NodeStatus.Added, kind: NodeKind.Directory),
            Entry("art/a.png", NodeStatus.Added),
            Entry("artefacts/x.png"),
            Entry("art/new.log", NodeStatus.Unversioned)
        );

        var confirmation = RevertConfirmation.For(rows[1], rows);

        await Assert.That(Paths(confirmation)).IsEqualTo("art,art/a.png,art/z.png");
        await Assert.That(confirmation.Title).IsEqualTo("Revert 3 paths in art?");
        await Assert
            .That(confirmation.Warning)
            .IsEqualTo("Nothing here is lost: it is only put back or un-scheduled.");
    }

    [Test]
    [Arguments(1, "1 of these loses work that cannot be brought back.")]
    [Arguments(2, "2 of these lose work that cannot be brought back.")]
    public async Task The_warning_counts_what_is_lost_for_good(int edited, string warning)
    {
        List<WorkingCopyEntry> entries = [Entry("d", NodeStatus.Deleted, kind: NodeKind.Directory)];
        entries.AddRange(Enumerable.Range(0, edited).Select(index => Entry($"d/e{index}.png")));
        entries.Add(Entry("d/gone.png", NodeStatus.Missing));
        var rows = Rows([.. entries]);

        await Assert.That(RevertConfirmation.For(rows[0], rows).Warning).IsEqualTo(warning);
    }

    /// <summary>One line that loses nothing is still a line: the case the rule's two halves meet on.</summary>
    [Test]
    public async Task A_single_line_that_loses_nothing_says_so()
    {
        var rows = Rows(Entry("gone.png", NodeStatus.Missing));

        await Assert
            .That(RevertConfirmation.For(rows[0], rows).Warning)
            .IsEqualTo("Nothing here is lost: it is only put back or un-scheduled.");
    }

    [Test]
    [Arguments(NodeStatus.Modified, true)]
    [Arguments(NodeStatus.Missing, false)]
    public async Task It_loses_work_exactly_when_one_of_its_lines_does(
        NodeStatus status,
        bool loses
    )
    {
        var rows = Rows(
            Entry("d", NodeStatus.Deleted, kind: NodeKind.Directory),
            Entry("d/a.png", status)
        );

        await Assert.That(RevertConfirmation.For(rows[0], rows).LosesWork).IsEqualTo(loses);
    }

    [Test]
    public async Task Something_revert_leaves_alone_has_nothing_to_confirm()
    {
        var rows = Rows(Entry("new.log", NodeStatus.Unversioned));

        await Assert.That(RevertConfirmation.For(rows[0], rows).Lines).IsEmpty();
    }

    /// <summary>A rename under the folder puts its old path back when that path is beneath it too.</summary>
    [Test]
    public async Task A_folder_lists_a_rename_by_its_old_path_only_when_that_path_is_beneath_it()
    {
        List<ChangeRow> rows =
        [
            ChangeRow.From(Entry("art", NodeStatus.Modified, kind: NodeKind.Directory)),
            ChangeRow.Rename(Entry("art/new.png", NodeStatus.Unversioned), "art/old.png"),
            ChangeRow.Rename(Entry("art/moved.png", NodeStatus.Unversioned), "src/moved.png"),
        ];

        var confirmation = RevertConfirmation.For(rows[0], rows);

        await Assert.That(Paths(confirmation)).IsEqualTo("art,art/old.png");
    }

    private static List<ChangeRow> Rows(params WorkingCopyEntry[] entries) =>
        [.. entries.Select(ChangeRow.From)];

    private static string Paths(RevertConfirmation confirmation) =>
        string.Join(",", confirmation.Lines.Select(line => line.RelPath));
}
