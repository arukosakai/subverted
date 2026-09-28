using Subverted.Core;

namespace Subverted.Frontend.Tests;

public sealed class IncomingCountTests
{
    [Test]
    public async Task Nothing_incoming_counts_nothing()
    {
        await Assert.That(IncomingCount.Of([])).IsEqualTo(0);
    }

    [Test]
    public async Task Each_file_marked_on_its_own_counts_once()
    {
        await Assert
            .That(IncomingCount.Of([Modified("src/a.txt"), Modified("art/hero.png")]))
            .IsEqualTo(2);
    }

    /// <summary>What 1.8.15 wrote for one new file: the file, and its folder marked as well.</summary>
    [Test]
    public async Task A_folder_marked_because_a_child_came_or_went_is_not_counted_beside_it()
    {
        await Assert
            .That(IncomingCount.Of([Added("art/villain.png"), Modified("art")]))
            .IsEqualTo(1);
    }

    /// <summary>The root is the empty path, and SVN marks it too when a top-level child is added.</summary>
    [Test]
    public async Task The_root_marked_for_a_top_level_child_is_not_counted_beside_it()
    {
        await Assert.That(IncomingCount.Of([Added("readme.txt"), Modified("")])).IsEqualTo(1);
    }

    [Test]
    public async Task The_root_marked_with_nothing_beneath_it_still_counts()
    {
        await Assert.That(IncomingCount.Of([Modified("")])).IsEqualTo(1);
    }

    [Test]
    public async Task A_folder_several_levels_up_from_the_mark_is_not_counted_either()
    {
        await Assert
            .That(IncomingCount.Of([Added("a/b/c/new.txt"), Modified("a"), Modified("a/b")]))
            .IsEqualTo(1);
    }

    /// <summary>A new folder is listed with each file in it, and the files are what arrive.</summary>
    [Test]
    public async Task A_new_folder_counts_its_files_and_not_itself()
    {
        await Assert
            .That(IncomingCount.Of([Added("newdir/1.txt"), Added("newdir/2.txt"), Added("newdir")]))
            .IsEqualTo(2);
    }

    /// <summary>1.8.15 lists a folder deleted on the server without its children.</summary>
    [Test]
    public async Task A_deleted_folder_with_nothing_listed_beneath_counts_once()
    {
        await Assert.That(IncomingCount.Of([Deleted("quiet"), Modified("")])).IsEqualTo(1);
    }

    [Test]
    public async Task A_folders_own_property_change_counts_even_with_marks_beneath_it()
    {
        await Assert
            .That(IncomingCount.Of([Added("art/villain.png"), PropertiesOnly("art")]))
            .IsEqualTo(2);
    }

    /// <summary>Ordinal prefixes are not folders: <c>art2</c> holds nothing of <c>art</c>'s.</summary>
    [Test]
    public async Task A_sibling_whose_name_starts_the_same_is_not_beneath_it()
    {
        await Assert
            .That(IncomingCount.Of([Modified("art"), Added("art2/a.png"), Added("art.bak/b.png")]))
            .IsEqualTo(3);
    }

    private static IncomingChange Added(string path) => new(path, PathChange.Added, false);

    private static IncomingChange Deleted(string path) => new(path, PathChange.Deleted, false);

    private static IncomingChange Modified(string path) => new(path, PathChange.Modified, false);

    private static IncomingChange PropertiesOnly(string path) => new(path, null, true);
}
