using System.Text;

namespace Subverted.Svn.Tests.ContextDiff;

public sealed class LineEndingNormalFormTests
{
    [Test]
    public async Task Without_an_eol_style_the_text_is_compared_as_it_is()
    {
        byte[] text = "a\r\nb\nc\r"u8.ToArray();

        await Assert
            .That(LineEndingNormalForm.Of(text, LineEndingStyle.AsCommitted))
            .IsSameReferenceAs(text);
    }

    [Test]
    [Arguments("a\r\nb\r\n", "native", "a\nb\n")]
    [Arguments("a\rb\r", "native", "a\nb\n")]
    [Arguments("a\nb\n", "native", "a\nb\n")]
    [Arguments("a\r\nb\r\n", "LF", "a\nb\n")]
    [Arguments("a\nb\n", "CRLF", "a\r\nb\r\n")]
    [Arguments("a\r\nb\r\n", "CR", "a\rb\r")]
    public async Task Every_ending_becomes_the_styles_own_whatever_kind_it_was(
        string text,
        string eolStyle,
        string normal
    )
    {
        var normalised = LineEndingNormalForm.Of(
            Encoding.ASCII.GetBytes(text),
            StyleNamed(eolStyle)
        );

        await Assert.That(Encoding.ASCII.GetString(normalised!)).IsEqualTo(normal);
    }

    [Test]
    public async Task A_last_line_without_an_ending_is_not_given_one()
    {
        var normalised = LineEndingNormalForm.Of("a\r\nb"u8.ToArray(), LineEndingStyle.Native);

        await Assert.That(Encoding.ASCII.GetString(normalised!)).IsEqualTo("a\nb");
    }

    [Test]
    public async Task A_text_with_no_endings_at_all_is_consistent()
    {
        var normalised = LineEndingNormalForm.Of("abc"u8.ToArray(), LineEndingStyle.CrLf);

        await Assert.That(Encoding.ASCII.GetString(normalised!)).IsEqualTo("abc");
    }

    private static LineEndingStyle StyleNamed(string eolStyle) =>
        ComparableText.LineEndingsOf([new SvnProperty("svn:eol-style", eolStyle)])!.Value;

    /// <summary>svn diff refuses such a file with E135000, measured on 1.8.15.</summary>
    [Test]
    [Arguments("a\r\nb\n")]
    [Arguments("a\nb\r")]
    [Arguments("a\rb\r\n")]
    public async Task Two_kinds_of_ending_in_one_file_have_no_normal_form(string text)
    {
        var normalised = LineEndingNormalForm.Of(
            Encoding.ASCII.GetBytes(text),
            LineEndingStyle.Native
        );

        await Assert.That(normalised).IsNull();
    }
}
