using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// SVN's diff text goes through untouched; only the classification is ours. The order of the
/// prefix checks is what these pin — <c>+++</c> is a file header, not an added line.
/// </summary>
public sealed class DiffReportTests
{
    [Test]
    public async Task A_working_copy_with_nothing_changed_says_so_rather_than_printing_nothing()
    {
        var lines = DiffReport.Lines(new DiffResponse(string.Empty), DiffPalette.Plain);

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0]).IsEqualTo("no local changes");
    }

    [Test]
    public async Task Svns_own_text_is_passed_through_line_for_line()
    {
        var lines = DiffReport.Lines(
            new DiffResponse("Index: a.txt\n@@ -1 +1 @@\n-old\n+new\n"),
            DiffPalette.Plain
        );

        await Assert
            .That(lines)
            .IsEquivalentTo(new[] { "Index: a.txt", "@@ -1 +1 @@", "-old", "+new" });
    }

    /// <summary>A trailing newline is a terminator, not an empty last line of the diff.</summary>
    [Test]
    [Arguments("+new\n")]
    [Arguments("+new\r\n")]
    [Arguments("+new")]
    public async Task The_final_newline_does_not_become_a_blank_line(string diff)
    {
        var lines = DiffReport.Lines(new DiffResponse(diff), DiffPalette.Plain);

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0]).IsEqualTo("+new");
    }

    /// <summary>
    /// The precedence, not each rule alone: <c>+++</c> and <c>---</c> start with <c>+</c> and
    /// <c>-</c>, and colouring a file header as content is what makes a diff unreadable.
    /// </summary>
    [Test]
    [Arguments("+++ b.txt\t(working copy)", DiffLine.FileHeader)]
    [Arguments("--- a.txt\t(revision 1)", DiffLine.FileHeader)]
    [Arguments("Index: a.txt", DiffLine.FileHeader)]
    [Arguments(
        "===================================================================",
        DiffLine.FileHeader
    )]
    [Arguments("@@ -1,3 +1,4 @@", DiffLine.HunkHeader)]
    [Arguments("+added line", DiffLine.Added)]
    [Arguments("-removed line", DiffLine.Removed)]
    [Arguments(" context line", DiffLine.Context)]
    [Arguments("\\ No newline at end of file", DiffLine.Context)]
    public async Task Each_line_is_classified_by_what_it_is(string line, DiffLine expected)
    {
        var painted = DiffReport.Lines(new DiffResponse(line), (text, kind) => $"<{kind}>{text}");

        await Assert.That(painted[0]).IsEqualTo($"<{expected}>{line}");
    }

    /// <summary>
    /// The property-change block SVN appends uses <c>##</c> rather than <c>@@</c>, so it is not a
    /// hunk header. Reading as context is right: nothing claims it is something it is not.
    /// </summary>
    [Test]
    public async Task A_property_hunk_marker_is_not_mistaken_for_a_content_hunk()
    {
        var painted = DiffReport.Lines(
            new DiffResponse("## -0,0 +1 ##"),
            (text, kind) => $"<{kind}>{text}"
        );

        await Assert.That(painted[0]).IsEqualTo("<Context>## -0,0 +1 ##");
    }
}
