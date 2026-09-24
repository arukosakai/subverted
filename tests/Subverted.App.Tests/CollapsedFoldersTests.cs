using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class CollapsedFoldersTests
{
    [Test]
    public async Task Everything_starts_expanded()
    {
        await Assert.That(new CollapsedFolders().Paths).IsEmpty();
    }

    [Test]
    public async Task A_collapsed_folder_is_remembered_until_it_is_expanded()
    {
        var collapsed = new CollapsedFolders();

        collapsed.Collapse("art");
        collapsed.Collapse("src");
        var afterCollapse = string.Join(",", collapsed.Paths.Order(StringComparer.Ordinal));
        collapsed.Expand("art");

        await Assert.That(afterCollapse).IsEqualTo("art,src");
        await Assert.That(collapsed.Paths).IsEquivalentTo(["src"]);
    }

    [Test]
    public async Task A_folder_still_holding_subfolders_stays_collapsed_across_a_fresh_tree()
    {
        var collapsed = new CollapsedFolders();
        collapsed.Collapse("art");

        collapsed.Follow(Tree("art/chars/hero.png", "art/chars/villain.png"));

        await Assert.That(collapsed.Paths).IsEquivalentTo(["art"]);
    }

    [Test]
    public async Task A_folder_gone_from_the_tree_is_forgotten()
    {
        var collapsed = new CollapsedFolders();
        collapsed.Collapse("art");

        collapsed.Follow(Tree("src/main/app.cs"));

        await Assert.That(collapsed.Paths).IsEmpty();
    }

    /// <summary>When its subfolders empty it has no chevron, and coming back it opens like a new one.</summary>
    [Test]
    public async Task A_folder_left_with_no_subfolders_is_forgotten()
    {
        var collapsed = new CollapsedFolders();
        collapsed.Collapse("art");

        collapsed.Follow(Tree("art/hero.png"));

        await Assert.That(collapsed.Paths).IsEmpty();
    }

    private static IReadOnlyList<FolderLine> Tree(params string[] relPaths) =>
        ChangeFolders.Of(
            [.. relPaths.Select(path => ChangeRow.From(Entry(path, NodeStatus.Modified)))],
            "game"
        );
}
