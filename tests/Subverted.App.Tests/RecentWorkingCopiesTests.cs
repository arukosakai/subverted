using Subverted.App.Presentation;
using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

public sealed class RecentWorkingCopiesTests
{
    [Test]
    public async Task The_copy_just_opened_goes_first()
    {
        var recent = RecentWorkingCopies.Opened(["/a", "/b"], "/c", StringComparison.Ordinal);

        await Assert.That(string.Join(",", recent)).IsEqualTo("/c,/a,/b");
    }

    [Test]
    public async Task Opening_one_already_listed_moves_it_rather_than_listing_it_twice()
    {
        var recent = RecentWorkingCopies.Opened(["/a", "/b", "/c"], "/b", StringComparison.Ordinal);

        await Assert.That(string.Join(",", recent)).IsEqualTo("/b,/a,/c");
    }

    /// <summary>On Windows <c>C:\Art</c> and <c>c:\art</c> are one checkout; elsewhere they are two.</summary>
    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, "/Art,/b")]
    [Arguments(StringComparison.Ordinal, "/Art,/art,/b")]
    public async Task Paths_are_told_apart_the_way_the_platform_tells_them_apart(
        StringComparison comparison,
        string expected
    )
    {
        var recent = RecentWorkingCopies.Opened(["/art", "/b"], "/Art", comparison);

        await Assert.That(string.Join(",", recent)).IsEqualTo(expected);
    }

    [Test]
    public async Task The_list_stops_at_its_capacity_dropping_the_oldest()
    {
        IReadOnlyList<string> full =
        [
            .. Enumerable.Range(0, RecentWorkingCopies.Capacity).Select(i => $"/{i}"),
        ];

        var recent = RecentWorkingCopies.Opened(full, "/new", StringComparison.Ordinal);

        await Assert.That(recent.Count).IsEqualTo(RecentWorkingCopies.Capacity);
        await Assert.That(recent[0]).IsEqualTo("/new");
        await Assert.That(recent).DoesNotContain($"/{RecentWorkingCopies.Capacity - 1}");
    }

    [Test]
    [Arguments("C:/studio/game/", "game")]
    [Arguments("/studio/game", "game")]
    [Arguments("/", "/")]
    public async Task A_copy_is_called_by_its_own_folder_name(string path, string expected)
    {
        await Assert.That(FolderName.Of(path)).IsEqualTo(expected);
    }
}
