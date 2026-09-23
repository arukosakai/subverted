using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;
using static Subverted.Frontend.Tests.Diff.Line;

namespace Subverted.Frontend.Tests.Diff;

/// <summary>
/// <see cref="FileDiff.Path"/> must equal the status row's <c>RelPath</c> for the same node. These
/// captures ran <c>svn diff</c> as the daemon does — from the root, on a root-relative target
/// spelled with Windows separators — so they show the spelling a real request gets back.
/// </summary>
public sealed class UnifiedDiffParserPathTests
{
    [Test]
    public async Task A_whole_working_copy_diff_lists_every_changed_path_once_in_svns_order()
    {
        var document = CapturedDiff.Parse("root.diff");

        await Assert
            .That(document.Files.Select(file => file.Path))
            .IsEquivalentTo(
                [
                    "added.txt",
                    "addeddir/inner.txt",
                    "bin-add.png",
                    "bin-del.bin",
                    "bin-mod-props.bin",
                    "bin-mod.bin",
                    "crlf-raw.txt",
                    "crlf-styled.txt",
                    "del-props.txt",
                    "del.txt",
                    "empty-props.txt",
                    "empty.txt",
                    "headers.txt",
                    "icon@2x.txt",
                    "mac-cr.txt",
                    "mergedir",
                    "mergemod",
                    "mod.txt",
                    "movededit.txt",
                    "moveedit.txt",
                    "moveme.txt",
                    "noeol-both.txt",
                    "noeol-new.txt",
                    "noeol-old.txt",
                    "propdir",
                    "propfile.txt",
                    "proponly.txt",
                    "replaced.txt",
                    "sub/a.txt",
                    "sub/b.txt",
                    "sub/d.txt",
                    "textprops.txt",
                    "trickyprop.txt",
                    "with space.txt",
                    "zażółć.txt",
                    "",
                ],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_directory_diff_names_each_file_beneath_it_relative_to_the_root()
    {
        var document = CapturedDiff.Parse("sub.diff");

        await Assert
            .That(document.Files.Select(file => file.Path))
            .IsEquivalentTo(["sub/a.txt", "sub/b.txt", "sub/d.txt"], CollectionOrdering.Matching);
        await ExpectedHunk.IsOnlyHunkOf(
            document.Files[0].Content,
            (1, 1, 1, 1),
            Removed("a", 1),
            Added("a edited", 1)
        );
        await ExpectedHunk.IsOnlyHunkOf(document.Files[1].Content, (1, 1, 0, 0), Removed("b", 1));
        await ExpectedHunk.IsOnlyHunkOf(document.Files[2].Content, (0, 0, 1, 1), Added("d", 1));
    }

    [Test]
    public async Task An_added_directory_contributes_its_files_but_no_entry_of_its_own()
    {
        var file = CapturedDiff.OnlyFile("addeddir.diff");

        await Assert.That(file.Path).IsEqualTo("addeddir/inner.txt");
        await ExpectedHunk.IsOnlyHunkOf(file.Content, (0, 0, 1, 1), Added("inside added dir", 1));
    }

    [Test]
    [Arguments("sub-a.diff", "sub/a.txt")]
    [Arguments("icon.diff", "icon@2x.txt")]
    [Arguments("polish.diff", "zażółć.txt")]
    [Arguments("with-space.diff", "with space.txt")]
    public async Task A_single_file_diff_names_the_file_as_its_status_row_does(
        string captureName,
        string expectedPath
    )
    {
        await Assert.That(CapturedDiff.OnlyFile(captureName).Path).IsEqualTo(expectedPath);
    }

    [Test]
    public async Task A_name_with_an_at_sign_is_diffed_whole_rather_than_split_at_a_peg_revision()
    {
        var file = CapturedDiff.OnlyFile("icon.diff");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 1, 1, 1),
            Removed("retina", 1),
            Added("retina edited", 1)
        );
    }

    [Test]
    public async Task A_non_ascii_name_arrives_intact_through_the_utf8_decoding_the_daemon_uses()
    {
        var file = CapturedDiff.OnlyFile("polish.diff");

        await ExpectedHunk.IsOnlyHunkOf(
            file.Content,
            (1, 1, 1, 1),
            Removed("polish", 1),
            Added("polish edited", 1)
        );
    }
}
