using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class FolderOutlineTests
{
    /// <summary>art has chars under it, and art2 and arx sit beside it with names that start alike.</summary>
    private static readonly IReadOnlyList<FolderLine> Tree = ChangeFolders.Of(
        [
            Row("art/chars/hero/idle.png"),
            Row("art/a.png"),
            Row("art2/b.png"),
            Row("arx/sub/c.png"),
            Row("src/main.cs"),
        ],
        "game"
    );

    [Test]
    public async Task With_nothing_collapsed_every_line_shows_and_none_is_marked()
    {
        var shown = FolderOutline.Shown(Tree, Collapsed());

        await Assert
            .That(Describe(shown))
            .IsEqualTo(",art,art/chars,art/chars/hero,art2,arx,arx/sub,src");
    }

    [Test]
    public async Task A_collapsed_folder_stays_marked_and_hides_every_line_at_any_depth_below_it()
    {
        var shown = FolderOutline.Shown(Tree, Collapsed("art"));

        await Assert.That(Describe(shown)).IsEqualTo(",art+,art2,arx,arx/sub,src");
    }

    /// <summary>arx/sub has a slash where art's name ends, but is not under art.</summary>
    [Test]
    public async Task A_folder_whose_name_only_starts_like_a_collapsed_one_is_not_hidden_by_it()
    {
        var shown = FolderOutline.Shown(Tree, Collapsed("art"));

        await Assert.That(shown.Select(line => line.RelPath)).Contains("arx/sub");
        await Assert.That(shown.Select(line => line.RelPath)).Contains("art2");
    }

    [Test]
    public async Task Collapsing_the_root_leaves_only_the_root()
    {
        var shown = FolderOutline.Shown(Tree, Collapsed(""));

        await Assert.That(Describe(shown)).IsEqualTo("+");
    }

    [Test]
    public async Task A_collapsed_folder_under_a_collapsed_one_is_hidden_with_the_rest()
    {
        var shown = FolderOutline.Shown(Tree, Collapsed("art", "art/chars"));

        await Assert.That(Describe(shown)).IsEqualTo(",art+,art2,arx,arx/sub,src");
    }

    [Test]
    public async Task A_collapsed_folder_under_an_open_one_shows_marked_and_hides_its_own()
    {
        var shown = FolderOutline.Shown(Tree, Collapsed("art/chars"));

        await Assert.That(Describe(shown)).IsEqualTo(",art,art/chars+,art2,arx,arx/sub,src");
    }

    [Test]
    public async Task A_folder_with_nothing_under_it_is_never_marked_collapsed()
    {
        var shown = FolderOutline.Shown(Tree, Collapsed("src"));

        await Assert.That(shown.Single(line => line.RelPath == "src").IsCollapsed).IsFalse();
    }

    [Test]
    public async Task A_selection_nothing_hides_stays_where_it_is()
    {
        await Assert
            .That(FolderOutline.Landing("art/chars", Collapsed("src")))
            .IsEqualTo("art/chars");
    }

    [Test]
    public async Task A_selection_below_a_collapsed_folder_lands_on_that_folder()
    {
        await Assert
            .That(FolderOutline.Landing("art/chars/hero", Collapsed("art")))
            .IsEqualTo("art");
    }

    [Test]
    public async Task A_selection_under_two_collapsed_folders_lands_on_the_outer_one_which_is_still_showing()
    {
        await Assert
            .That(FolderOutline.Landing("art/chars/hero", Collapsed("art/chars", "art")))
            .IsEqualTo("art");
    }

    [Test]
    public async Task A_collapsed_folder_that_is_itself_selected_keeps_the_selection()
    {
        await Assert.That(FolderOutline.Landing("art", Collapsed("art"))).IsEqualTo("art");
    }

    [Test]
    [Arguments("art2")]
    [Arguments("arx/sub")]
    public async Task A_selection_beside_a_collapsed_folder_with_a_like_name_stays(string selected)
    {
        await Assert.That(FolderOutline.Landing(selected, Collapsed("art"))).IsEqualTo(selected);
    }

    [Test]
    public async Task A_collapsed_root_takes_any_selection_below_it()
    {
        await Assert.That(FolderOutline.Landing("src", Collapsed(""))).IsEqualTo("");
    }

    [Test]
    public async Task The_root_selected_under_a_collapsed_root_stays_on_the_root()
    {
        await Assert.That(FolderOutline.Landing("", Collapsed(""))).IsEqualTo("");
    }

    private static HashSet<string> Collapsed(params string[] relPaths) =>
        new(relPaths, StringComparer.Ordinal);

    /// <summary>Each shown line's path, a collapsed one with a trailing +.</summary>
    private static string Describe(IEnumerable<FolderLine> shown) =>
        string.Join(",", shown.Select(line => line.RelPath + (line.IsCollapsed ? "+" : "")));

    private static ChangeRow Row(string relPath) =>
        ChangeRow.From(Entry(relPath, NodeStatus.Modified));
}
