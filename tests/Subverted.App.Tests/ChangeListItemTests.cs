using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class ChangeListItemTests
{
    [Test]
    public async Task A_flat_line_is_the_row_as_it_is_at_no_depth()
    {
        var row = ChangeRow.From(Entry("art/ui/button.png"));

        var item = ChangeListItem.Flat(row);

        await Assert
            .That(item)
            .IsEqualTo(new ChangeListItem("art/ui/button.png", "button.png", "art/ui", 0, row));
    }

    [Test]
    public async Task A_change_is_keyed_by_its_path()
    {
        var item = ChangeListItem.Flat(ChangeRow.From(Entry("art")));

        await Assert.That(item.Key).IsEqualTo("art");
    }

    /// <summary>So a folder that only holds changes cannot collide with a pinned change at the same path.</summary>
    [Test]
    public async Task A_folder_that_only_holds_changes_is_keyed_with_a_trailing_slash()
    {
        var item = new ChangeListItem("art", "art", "", 0, null);

        await Assert.That(item.Key).IsEqualTo("art/");
    }

    [Test]
    public async Task A_screen_reader_hears_the_name_the_folder_and_the_badge()
    {
        var item = ChangeListItem.Flat(ChangeRow.From(Entry("art/hero.png", NodeStatus.Added)));

        await Assert.That(item.AutomationName).IsEqualTo("hero.png in art, Added");
    }

    /// <summary>The tree drops the faint folder text, since the indent shows it — but an indent is not heard.</summary>
    [Test]
    public async Task A_tree_line_still_says_its_folder_aloud()
    {
        var row = ChangeRow.From(Entry("art/hero.png"));

        var item = new ChangeListItem("art/hero.png", "hero.png", "", 1, row);

        await Assert.That(item.AutomationName).IsEqualTo("hero.png in art, Modified");
    }

    [Test]
    public async Task A_change_at_the_root_says_no_folder()
    {
        var item = ChangeListItem.Flat(ChangeRow.From(Entry("readme.txt", NodeStatus.Deleted)));

        await Assert.That(item.AutomationName).IsEqualTo("readme.txt, Deleted");
    }

    [Test]
    public async Task A_folder_line_says_it_is_a_folder()
    {
        var item = new ChangeListItem("art/ui", "ui", "", 1, null);

        await Assert.That(item.AutomationName).IsEqualTo("ui, folder");
    }
}
