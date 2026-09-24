using Subverted.App.Presentation;
using Subverted.Core;

namespace Subverted.App.Tests;

public sealed class ChangedPathRowTests
{
    [Test]
    public async Task A_path_splits_into_its_name_and_the_folder_above_it()
    {
        var row = ChangedPathRow.From(Changed("/trunk/art/hero.png"));

        await Assert.That(row.Path).IsEqualTo("/trunk/art/hero.png");
        await Assert.That(row.Name).IsEqualTo("hero.png");
        await Assert.That(row.Folder).IsEqualTo("/trunk/art");
    }

    [Test]
    public async Task A_path_directly_under_the_root_has_no_folder()
    {
        var row = ChangedPathRow.From(Changed("/readme.txt"));

        await Assert.That(row.Name).IsEqualTo("readme.txt");
        await Assert.That(row.Folder).IsEmpty();
    }

    [Test]
    public async Task The_repository_root_is_named_as_a_slash()
    {
        var row = ChangedPathRow.From(Changed("/"));

        await Assert.That(row.Name).IsEqualTo("/");
        await Assert.That(row.Folder).IsEmpty();
    }

    [Test]
    public async Task A_trailing_slash_does_not_leave_the_name_empty()
    {
        var row = ChangedPathRow.From(Changed("/branches/feature/"));

        await Assert.That(row.Name).IsEqualTo("feature");
        await Assert.That(row.Folder).IsEqualTo("/branches");
    }

    [Test]
    [Arguments(PathChange.Added, "Added", ChangeTone.Added)]
    [Arguments(PathChange.Deleted, "Deleted", ChangeTone.Deleted)]
    [Arguments(PathChange.Modified, "Modified", ChangeTone.Modified)]
    [Arguments(PathChange.Replaced, "Replaced", ChangeTone.Replaced)]
    public async Task Each_kind_of_change_has_its_own_badge(
        PathChange change,
        string label,
        ChangeTone tone
    )
    {
        var row = ChangedPathRow.From(new ChangedPath("/a.txt", change, null, null));

        await Assert.That(row.Badge).IsEqualTo(new ChangeBadge(label, tone));
    }

    [Test]
    public async Task A_change_this_build_has_never_heard_of_is_refused_rather_than_drawn_as_another()
    {
        await Assert
            .That(() => ChangedPathRow.From(new ChangedPath("/a.txt", (PathChange)99, null, null)))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task A_copy_names_where_it_came_from_and_at_which_revision()
    {
        var row = ChangedPathRow.From(new ChangedPath("/b.txt", PathChange.Added, "/a.txt", 2));

        await Assert.That(row.CopiedFrom).IsEqualTo("/a.txt@2");
    }

    [Test]
    public async Task A_plain_add_names_no_source()
    {
        var row = ChangedPathRow.From(new ChangedPath("/b.txt", PathChange.Added, null, null));

        await Assert.That(row.CopiedFrom).IsNull();
    }

    /// <summary>The two are promised together; half of one is not shown as a whole.</summary>
    [Test]
    public async Task A_source_path_without_its_revision_is_not_shown()
    {
        var row = ChangedPathRow.From(new ChangedPath("/b.txt", PathChange.Added, "/a.txt", null));

        await Assert.That(row.CopiedFrom).IsNull();
    }

    [Test]
    public async Task A_screen_reader_hears_the_name_the_folder_and_the_badge()
    {
        var row = ChangedPathRow.From(
            new ChangedPath("/src/Player.cs", PathChange.Added, null, null)
        );

        await Assert.That(row.AutomationName).IsEqualTo("Player.cs in /src, Added");
    }

    [Test]
    public async Task A_path_under_the_root_says_no_folder_aloud()
    {
        var row = ChangedPathRow.From(
            new ChangedPath("/readme.txt", PathChange.Deleted, null, null)
        );

        await Assert.That(row.AutomationName).IsEqualTo("readme.txt, Deleted");
    }

    [Test]
    public async Task A_copy_says_where_it_came_from_aloud()
    {
        var row = ChangedPathRow.From(new ChangedPath("/b.txt", PathChange.Added, "/a.txt", 2));

        await Assert.That(row.AutomationName).IsEqualTo("b.txt, Added, from /a.txt@2");
    }

    private static ChangedPath Changed(string path) => new(path, PathChange.Modified, null, null);
}
