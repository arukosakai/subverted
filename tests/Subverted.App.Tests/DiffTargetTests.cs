using Subverted.App.ViewModels;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>Where a row is on disk, which is what the daemon is asked about.</summary>
public sealed class DiffTargetTests
{
    private static readonly string Root = Info.RootPath;
    private static readonly char Separator = Path.DirectorySeparatorChar;

    [Test]
    public async Task The_root_row_names_the_root_itself()
    {
        await Assert.That(DiffTarget.PathOf(Root, "")).IsEqualTo(Root);
    }

    [Test]
    public async Task A_row_at_the_top_level_is_one_segment_under_the_root()
    {
        await Assert
            .That(DiffTarget.PathOf(Root, "readme.txt"))
            .IsEqualTo($"{Root}{Separator}readme.txt");
    }

    [Test]
    public async Task A_nested_row_is_joined_under_the_root_in_the_platforms_separator()
    {
        await Assert
            .That(DiffTarget.PathOf(Root, "art/hero.png"))
            .IsEqualTo($"{Root}{Separator}art{Separator}hero.png");
    }
}
