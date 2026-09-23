using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class RenameCaptionTests
{
    [Test]
    [Arguments("art/hero.png", "art/protagonist.png", "← hero.png")]
    [Arguments("hero.png", "protagonist.png", "← hero.png")]
    [Arguments("art/hero.png", "art/chars/hero.png", "← art/hero.png")]
    [Arguments("hero.png", "art/hero.png", "← hero.png")]
    [Arguments("art/hero.png", "hero.png", "← art/hero.png")]
    public async Task A_rename_in_one_folder_names_the_old_file_and_a_move_names_the_old_path(
        string from,
        string to,
        string caption
    )
    {
        await Assert.That(RenameCaption.For(from, to)).IsEqualTo(caption);
    }

    [Test]
    public async Task Only_a_rename_row_has_a_caption()
    {
        var rename = ChangeRow.Rename(Entry("art/b.png", NodeStatus.Unversioned), "art/a.png");

        await Assert.That(rename.RenameCaption).IsEqualTo("← a.png");
        await Assert.That(ChangeRow.From(Entry("art/b.png")).RenameCaption).IsNull();
    }
}
