namespace Subverted.Svn.Tests.ContextDiff;

/// <summary>
/// The pure half of the in-process diff, fed the exact bytes svn compared, must write exactly the
/// bytes svn wrote — at svn's three lines, the one amount there is anything to compare against.
/// Byte equality is stronger than the parsed documents being equal, which it implies.
/// </summary>
public sealed class CapturedEquivalenceTests
{
    private const string WindowsNewline = "\r\n";

    [Test]
    [Arguments("plain.txt", "")]
    [Arguments("gap5.txt", "")]
    [Arguments("gap6.txt", "")]
    [Arguments("crlf-noprop.txt", "")]
    [Arguments("mixed-noprop.txt", "")]
    [Arguments("lone-cr.txt", "")]
    [Arguments("native.txt", "native")]
    [Arguments("native-lf-on-disk.txt", "native")]
    [Arguments("lf-style.txt", "LF")]
    [Arguments("crlf-style.txt", "CRLF")]
    [Arguments("cr-style.txt", "CR")]
    [Arguments("text-mime.txt", "")]
    [Arguments("empty.txt", "")]
    [Arguments("becomes-empty.txt", "")]
    [Arguments("no-eol.txt", "")]
    [Arguments("gains-no-eol.txt", "")]
    [Arguments("no-eol-context.txt", "")]
    [Arguments("ambiguous.txt", "")]
    [Arguments("swap.txt", "")]
    [Arguments("tie-insertion.txt", "")]
    [Arguments("tie-deletion.txt", "")]
    public async Task A_local_edit_is_written_as_svn_diff_wrote_it(string name, string eolStyle)
    {
        var svn = Capture(name, ".diff");
        var working = LineEndingNormalForm.Of(Capture(name, ".working"), StyleOf(eolStyle))!;

        var written = SvnStyleDiff.Write(
            new DiffSectionHeader(name, "revision 2", "working copy"),
            Capture(name, ".base"),
            working,
            3,
            WindowsNewline
        );

        await Assert.That(svn).IsNotEmpty();
        await Assert.That(Bytes(written)).IsEqualTo(Bytes(svn));
    }

    [Test]
    [Arguments("plain.txt", "")]
    [Arguments("crlf-noprop.txt", "")]
    [Arguments("mixed-noprop.txt", "")]
    [Arguments("lone-cr.txt", "")]
    [Arguments("native.txt", "native")]
    [Arguments("lf-style.txt", "LF")]
    [Arguments("crlf-style.txt", "CRLF")]
    [Arguments("cr-style.txt", "CR")]
    [Arguments("empty.txt", "")]
    [Arguments("becomes-empty.txt", "")]
    [Arguments("no-eol.txt", "")]
    [Arguments("gains-no-eol.txt", "")]
    [Arguments("ambiguous.txt", "")]
    [Arguments("swap.txt", "")]
    public async Task A_committed_edit_read_back_by_svn_cat_is_written_as_svn_diff_c_wrote_it(
        string name,
        string eolStyle
    )
    {
        var svn = Capture(name, ".rev.diff");
        var style = StyleOf(eolStyle);

        var written = SvnStyleDiff.Write(
            new DiffSectionHeader(name, "revision 1", "revision 2"),
            LineEndingNormalForm.Of(Capture(name, ".r1"), style)!,
            LineEndingNormalForm.Of(Capture(name, ".r2"), style)!,
            3,
            WindowsNewline
        );

        await Assert.That(svn).IsNotEmpty();
        await Assert.That(Bytes(written)).IsEqualTo(Bytes(svn));
    }

    /// <summary>
    /// <c>svn cat</c> hands a native file back with this platform's endings, not the pristine's —
    /// which is why History normalises both sides before comparing.
    /// </summary>
    [Test]
    public async Task Svn_cat_gives_a_native_file_the_platforms_endings_and_not_its_normal_form()
    {
        var pristine = Capture("native.txt", ".base");

        await Assert
            .That(Bytes(Capture("native.txt", ".r2")))
            .IsEqualTo(Bytes(LineEndingNormalForm.Of(pristine, LineEndingStyle.CrLf)));
        await Assert.That(pristine).DoesNotContain((byte)'\r');
    }

    /// <summary>Bytes as one character each, so a mismatch reads as text and order counts.</summary>
    private static string? Bytes(byte[]? bytes) =>
        bytes is null ? null : System.Text.Encoding.Latin1.GetString(bytes);

    /// <summary>The style the file's properties give it, read the way the daemon reads them.</summary>
    private static LineEndingStyle StyleOf(string eolStyle) =>
        ComparableText
            .LineEndingsOf(
                eolStyle.Length == 0 ? [] : [new SvnProperty("svn:eol-style", eolStyle)]
            )!
            .Value;

    private static byte[] Capture(string name, string extension) =>
        File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "ContextDiff", "Captures", name + extension)
        );
}
