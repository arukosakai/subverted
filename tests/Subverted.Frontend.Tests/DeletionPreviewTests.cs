using Subverted.Core;
using static Subverted.Frontend.Tests.Nodes;

namespace Subverted.Frontend.Tests;

/// <summary>The list read before a delete: every node it reaches, what is lost for good first.</summary>
public sealed class DeletionPreviewTests
{
    private const StringComparison Ordinal = StringComparison.Ordinal;

    [Test]
    public async Task A_file_s_preview_is_its_own_line()
    {
        var preview = DeletionPreview.Of("a.png", [Node("a.png"), Node("b.png")], Ordinal);

        await Assert.That(preview.Target).IsEqualTo("a.png");
        await Assert
            .That(preview.Lines.Select(line => line.RelPath))
            .IsEquivalentTo(new[] { "a.png" });
    }

    [Test]
    public async Task A_folder_s_preview_reaches_everything_beneath_it_and_nothing_beside_it()
    {
        WorkingCopyEntry[] listing =
        [
            Node("art", kind: NodeKind.Directory),
            Node("art/a.png"),
            Node("art/sub/b.png"),
            Node("artefacts/c.png", NodeStatus.Modified),
            Node("docs/d.txt", NodeStatus.Unversioned),
        ];

        var preview = DeletionPreview.Of("art", listing, Ordinal);

        await Assert
            .That(preview.Lines.Select(line => line.RelPath))
            .IsEquivalentTo(new[] { "art", "art/a.png", "art/sub/b.png" });
    }

    [Test]
    public async Task What_is_lost_for_good_comes_first_then_the_rest_each_in_path_order()
    {
        WorkingCopyEntry[] listing =
        [
            Node("art/z.png", NodeStatus.Modified),
            Node("art", kind: NodeKind.Directory),
            Node("art/b.png"),
            Node("art/build.log", NodeStatus.Ignored),
            Node("art/a.png"),
        ];

        var preview = DeletionPreview.Of("art", listing, Ordinal);

        await Assert
            .That(string.Join(",", preview.Lines.Select(line => line.RelPath)))
            .IsEqualTo("art/build.log,art/z.png,art,art/a.png,art/b.png");
    }

    [Test]
    public async Task A_node_already_marked_deleted_is_not_listed()
    {
        WorkingCopyEntry[] listing =
        [
            Node("art", kind: NodeKind.Directory),
            Node("art/old.png", NodeStatus.Deleted),
        ];

        var preview = DeletionPreview.Of("art", listing, Ordinal);

        await Assert
            .That(preview.Lines.Select(line => line.RelPath))
            .IsEquivalentTo(new[] { "art" });
    }

    [Test]
    [Arguments(StringComparison.Ordinal, 1)]
    [Arguments(StringComparison.OrdinalIgnoreCase, 2)]
    public async Task What_lies_beneath_is_decided_by_the_platform_s_comparison(
        StringComparison comparison,
        int lines
    )
    {
        WorkingCopyEntry[] listing = [Node("art", kind: NodeKind.Directory), Node("Art/a.png")];

        await Assert
            .That(DeletionPreview.Of("art", listing, comparison).Lines)
            .Count()
            .IsEqualTo(lines);
    }

    [Test]
    public async Task One_line_is_asked_about_by_name()
    {
        var preview = DeletionPreview.Of("art/a.png", [Node("art/a.png")], Ordinal);

        await Assert.That(preview.Title).IsEqualTo("Delete art/a.png?");
    }

    [Test]
    public async Task Many_lines_are_counted_in_the_folder_they_are_in()
    {
        WorkingCopyEntry[] listing =
        [
            Node("art", kind: NodeKind.Directory),
            .. Enumerable.Range(0, 1233).Select(index => Node($"art/{index}.png")),
        ];

        await Assert
            .That(DeletionPreview.Of("art", listing, Ordinal).Title)
            .IsEqualTo("Delete 1,234 paths in art?");
    }

    [Test]
    public async Task Nothing_lost_is_said_plainly()
    {
        WorkingCopyEntry[] listing =
        [
            Node("art", kind: NodeKind.Directory),
            Node("art/gone.png", NodeStatus.Missing),
        ];

        await Assert
            .That(DeletionPreview.Of("art", listing, Ordinal).Warning)
            .IsEqualTo("Nothing here is lost: SVN still has all of it.");
    }

    [Test]
    public async Task A_single_line_that_loses_work_is_warned_about_as_one()
    {
        var preview = DeletionPreview.Of("a.png", [Node("a.png", NodeStatus.Added)], Ordinal);

        await Assert.That(preview.Warning).IsEqualTo("What it deletes cannot be brought back.");
    }

    [Test]
    public async Task One_loss_among_several_lines_is_counted()
    {
        WorkingCopyEntry[] listing =
        [
            Node("art", kind: NodeKind.Directory),
            Node("art/a.png", NodeStatus.Modified),
        ];

        await Assert
            .That(DeletionPreview.Of("art", listing, Ordinal).Warning)
            .IsEqualTo("1 of these loses work that cannot be brought back.");
    }

    [Test]
    public async Task Many_losses_are_counted()
    {
        WorkingCopyEntry[] listing =
        [
            Node("art", kind: NodeKind.Directory),
            .. Enumerable
                .Range(0, 1200)
                .Select(index => Node($"art/{index}.tmp", NodeStatus.Unversioned)),
        ];

        await Assert
            .That(DeletionPreview.Of("art", listing, Ordinal).Warning)
            .IsEqualTo("1,200 of these lose work that cannot be brought back.");
    }

    [Test]
    public async Task The_same_lines_for_the_same_target_are_the_same_question()
    {
        WorkingCopyEntry[] listing = [Node("art", kind: NodeKind.Directory), Node("art/a.png")];

        var first = DeletionPreview.Of("art", listing, Ordinal);
        var second = DeletionPreview.Of("art", [.. listing], Ordinal);

        await Assert.That(first.IsSameQuestionAs(second)).IsTrue();
    }

    [Test]
    public async Task A_line_that_changed_what_it_loses_is_a_different_question()
    {
        var clean = DeletionPreview.Of("a.png", [Node("a.png")], Ordinal);
        var edited = DeletionPreview.Of("a.png", [Node("a.png", NodeStatus.Modified)], Ordinal);

        await Assert.That(clean.IsSameQuestionAs(edited)).IsFalse();
    }

    [Test]
    public async Task A_line_more_is_a_different_question()
    {
        var before = DeletionPreview.Of("art", [Node("art", kind: NodeKind.Directory)], Ordinal);
        var after = DeletionPreview.Of(
            "art",
            [Node("art", kind: NodeKind.Directory), Node("art/new.psd", NodeStatus.Unversioned)],
            Ordinal
        );

        await Assert.That(before.IsSameQuestionAs(after)).IsFalse();
    }

    [Test]
    public async Task The_same_lines_for_another_target_are_a_different_question()
    {
        var first = new DeletionPreview("art", []);
        var second = new DeletionPreview("docs", []);

        await Assert.That(first.IsSameQuestionAs(second)).IsFalse();
    }
}
