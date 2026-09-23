using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>Each line is written as key, depth, then "=" for a change or "/" for a folder that only holds them.</summary>
public sealed class ChangeTreeTests
{
    [Test]
    public async Task Every_folder_on_the_way_down_gets_a_line_above_what_it_holds()
    {
        var tree = ChangeTree.Of([Row("art/ui/button.png"), Row("readme.txt")]);

        await Assert
            .That(Lines(tree))
            .IsEqualTo("art/ 0 /|art/ui/ 1 /|art/ui/button.png 2 =|readme.txt 0 =");
    }

    [Test]
    public async Task A_folder_holding_several_changes_is_listed_once()
    {
        var tree = ChangeTree.Of([Row("art/a.png"), Row("art/b.png")]);

        await Assert.That(Lines(tree)).IsEqualTo("art/ 0 /|art/a.png 1 =|art/b.png 1 =");
    }

    /// <summary>An added directory is itself a change, so its line is the change, not a second folder line.</summary>
    [Test]
    public async Task A_changed_folder_is_the_line_its_contents_hang_under()
    {
        var tree = ChangeTree.Of([
            Row("art", NodeStatus.Added),
            Row("art/a.png", NodeStatus.Added),
        ]);

        await Assert.That(Lines(tree)).IsEqualTo("art 0 =|art/a.png 1 =");
    }

    /// <summary>Plain ordinal order would put <c>art-old</c> between <c>art</c> and its contents.</summary>
    [Test]
    public async Task A_folder_s_contents_sit_directly_under_it_before_a_sibling_that_sorts_close()
    {
        var tree = ChangeTree.Of([Row("art-old.png"), Row("art/a.png")]);

        await Assert.That(Lines(tree)).IsEqualTo("art/ 0 /|art/a.png 1 =|art-old.png 0 =");
    }

    [Test]
    public async Task The_root_s_own_change_comes_first_at_the_top_level()
    {
        var tree = ChangeTree.Of([
            Row("a.txt"),
            Row("", NodeStatus.Unmodified, PropertyStatus.Modified),
        ]);

        await Assert.That(Lines(tree)).IsEqualTo(" 0 =|a.txt 0 =");
        await Assert.That(tree[0].Name).IsEqualTo(".");
    }

    [Test]
    public async Task A_tree_line_reads_its_own_name_and_leaves_the_folder_to_the_indent()
    {
        var tree = ChangeTree.Of([Row("art/ui/button.png")]);

        await Assert
            .That(tree.Select(item => $"{item.Name}:{item.Folder}"))
            .IsEquivalentTo(new[] { "art:", "ui:", "button.png:" });
    }

    [Test]
    public async Task Pinned_rows_sit_above_the_tree_as_flat_lines_in_the_order_given()
    {
        var conflict = Row("src/deep/clash.cs", NodeStatus.Conflicted);
        var missing = Row("art/gone.png", NodeStatus.Missing);

        var tree = ChangeTree.Of([conflict, missing, Row("art/a.png")]);

        await Assert
            .That(Lines(tree))
            .IsEqualTo("src/deep/clash.cs 0 =|art/gone.png 0 =|art/ 0 /|art/a.png 1 =");
        await Assert.That(tree[0]).IsEqualTo(ChangeListItem.Flat(conflict));
    }

    /// <summary>A pinned folder's contents still nest under a folder line, which the trailing slash keeps distinct.</summary>
    [Test]
    public async Task A_pinned_folder_and_the_folder_line_for_its_contents_have_different_keys()
    {
        var tree = ChangeTree.Of([Row("lib", NodeStatus.Conflicted), Row("lib/x.c")]);

        await Assert.That(Lines(tree)).IsEqualTo("lib 0 =|lib/ 0 /|lib/x.c 1 =");
    }

    private static ChangeRow Row(
        string relPath,
        NodeStatus status = NodeStatus.Modified,
        PropertyStatus properties = PropertyStatus.Unmodified
    ) => ChangeRow.From(Entry(relPath, status, properties));

    private static string Lines(IEnumerable<ChangeListItem> items) =>
        string.Join(
            "|",
            items.Select(item => $"{item.Key} {item.Depth} {(item.Row is null ? "/" : "=")}")
        );
}
