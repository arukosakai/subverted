using Subverted.App.Presentation;
using Subverted.Frontend.Diff;

namespace Subverted.App.Tests;

public sealed class DiffRowTests
{
    [Test]
    [Arguments(DiffLineKind.Added, "+")]
    [Arguments(DiffLineKind.Removed, "−")]
    [Arguments(DiffLineKind.Context, "")]
    [Arguments((DiffLineKind)99, "")]
    public async Task A_line_is_signed_as_a_unified_diff_signs_it(DiffLineKind kind, string sign)
    {
        var row = new DiffTextRow(new DiffLine(kind, "x", 1, 1, EndsWithoutNewline: false));

        await Assert.That(row.Sign).IsEqualTo(sign);
    }

    [Test]
    [Arguments(PropertyChangeKind.Added, "Added", ChangeTone.Added)]
    [Arguments(PropertyChangeKind.Modified, "Modified", ChangeTone.Modified)]
    [Arguments(PropertyChangeKind.Deleted, "Deleted", ChangeTone.Deleted)]
    [Arguments((PropertyChangeKind)99, "99", ChangeTone.Quiet)]
    public async Task A_property_change_is_labelled_and_toned_like_a_file_change(
        PropertyChangeKind kind,
        string label,
        ChangeTone tone
    )
    {
        var row = new DiffPropertyRow("svn:keywords", kind);

        await Assert.That(row.Label).IsEqualTo(label);
        await Assert.That(row.Tone).IsEqualTo(tone);
    }

    [Test]
    [Arguments("image/png", "image/png")]
    [Arguments(null, "No MIME type recorded")]
    public async Task A_binary_card_names_the_mime_type_or_says_there_is_none(
        string? mimeType,
        string text
    )
    {
        await Assert
            .That(new DiffBinaryRow("a.bin", mimeType, IsOnlyFile: true).MimeTypeText)
            .IsEqualTo(text);
    }
}
