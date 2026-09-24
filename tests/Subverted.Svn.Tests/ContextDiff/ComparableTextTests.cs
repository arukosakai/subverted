namespace Subverted.Svn.Tests.ContextDiff;

public sealed class ComparableTextTests
{
    [Test]
    public async Task A_file_with_no_properties_is_compared_as_committed()
    {
        await Assert.That(ComparableText.LineEndingsOf([])).IsEqualTo(LineEndingStyle.AsCommitted);
    }

    [Test]
    public async Task Properties_that_change_nothing_on_disk_leave_it_comparable()
    {
        var style = ComparableText.LineEndingsOf(
            [new SvnProperty("svn:needs-lock", "*"), new SvnProperty("svn:executable", "*")]
        );

        await Assert.That(style).IsEqualTo(LineEndingStyle.AsCommitted);
    }

    [Test]
    [Arguments("native", 1)]
    [Arguments("LF", 2)]
    [Arguments("CRLF", 3)]
    [Arguments("CR", 4)]
    public async Task Each_eol_style_svn_accepts_names_its_line_endings(string value, int style)
    {
        var endings = ComparableText.LineEndingsOf([new SvnProperty("svn:eol-style", value)]);

        await Assert.That(endings).IsEqualTo((LineEndingStyle)style);
    }

    [Test]
    [Arguments("crlf")]
    [Arguments("Native")]
    [Arguments("")]
    public async Task An_eol_style_svn_would_not_accept_is_not_guessed_at(string value)
    {
        await Assert.That(ComparableText.LineEndingsOf([new SvnProperty("svn:eol-style", value)])).IsNull();
    }

    [Test]
    [Arguments("svn:keywords", "Id")]
    [Arguments("svn:special", "*")]
    [Arguments("svn:mime-type", "application/octet-stream")]
    [Arguments("svn:mime-type", "image/png")]
    [Arguments("svn:mime-type", "textual/x")]
    public async Task A_property_that_makes_svn_answer_otherwise_is_left_to_svn(string name, string value)
    {
        await Assert.That(ComparableText.LineEndingsOf([new SvnProperty(name, value)])).IsNull();
    }

    [Test]
    [Arguments("text/plain")]
    [Arguments("text/html")]
    public async Task A_text_mime_type_is_text_to_svn_too(string value)
    {
        var style = ComparableText.LineEndingsOf([new SvnProperty("svn:mime-type", value)]);

        await Assert.That(style).IsEqualTo(LineEndingStyle.AsCommitted);
    }

    [Test]
    public async Task Keywords_are_refused_even_after_an_eol_style_was_read()
    {
        var style = ComparableText.LineEndingsOf(
            [new SvnProperty("svn:eol-style", "native"), new SvnProperty("svn:keywords", "Id")]
        );

        await Assert.That(style).IsNull();
    }

    [Test]
    public async Task An_eol_style_with_a_text_mime_type_keeps_its_line_endings()
    {
        var style = ComparableText.LineEndingsOf(
            [new SvnProperty("svn:mime-type", "text/plain"), new SvnProperty("svn:eol-style", "CRLF")]
        );

        await Assert.That(style).IsEqualTo(LineEndingStyle.CrLf);
    }
}
