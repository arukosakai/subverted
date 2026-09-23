namespace Subverted.Svn.Tests;

public sealed class GlobalIgnoreConfigurationTests
{
    /// <summary>
    /// Run against a throwaway <c>--config-dir</c> holding exactly this, SVN 1.8.15 ignored
    /// <c>note.bak</c> and <c>custom-thing.txt</c> and stopped ignoring <c>t.o</c> — so a
    /// configured value replaces the default rather than extending it.
    /// </summary>
    [Test]
    public async Task A_configured_value_replaces_the_default()
    {
        var patterns = Parse("[miscellany]", "global-ignores = *.bak custom-*");

        await Assert.That(Join(patterns)).IsEqualTo("*.bak|custom-*");
    }

    /// <summary>
    /// The value is whitespace-delimited here, unlike the newline-delimited properties of the
    /// same name.
    /// </summary>
    [Test]
    public async Task The_value_is_whitespace_delimited()
    {
        var patterns = Parse("[miscellany]", "global-ignores = *.o\t*.a   *.so");

        await Assert.That(Join(patterns)).IsEqualTo("*.o|*.a|*.so");
    }

    /// <summary>An indented line continues the setting above it — the stock file ships that way.</summary>
    [Test]
    public async Task An_indented_line_continues_the_setting()
    {
        var patterns = Parse("[miscellany]", "global-ignores = *.o *.lo", "   *.rej *~");

        await Assert.That(Join(patterns)).IsEqualTo("*.o|*.lo|*.rej|*~");
    }

    [Test]
    public async Task A_continuation_stops_at_the_next_setting()
    {
        var patterns = Parse(
            "[miscellany]",
            "global-ignores = *.o",
            "   *.lo",
            "log-encoding = latin1",
            "   *.never"
        );

        await Assert.That(Join(patterns)).IsEqualTo("*.o|*.lo");
    }

    /// <summary>This is how the setting ships, and why the default is what a stock box uses.</summary>
    [Test]
    [Arguments("# global-ignores = *.bak")]
    [Arguments("; global-ignores = *.bak")]
    public async Task A_commented_out_setting_leaves_the_default_in_place(string line)
    {
        var patterns = Parse("[miscellany]", line, "#   *.continuation");

        await Assert.That(patterns).IsEqualTo(GlobalIgnoreConfiguration.SubversionDefault);
    }

    [Test]
    public async Task The_setting_only_counts_inside_the_miscellany_section()
    {
        var patterns = Parse("[helpers]", "global-ignores = *.bak");

        await Assert.That(patterns).IsEqualTo(GlobalIgnoreConfiguration.SubversionDefault);
    }

    [Test]
    public async Task Leaving_a_later_section_stops_the_setting_being_read()
    {
        var patterns = Parse(
            "[miscellany]",
            "global-ignores = *.bak",
            "[helpers]",
            "global-ignores = *.never"
        );

        await Assert.That(Join(patterns)).IsEqualTo("*.bak");
    }

    /// <summary>
    /// Explicitly blank means "ignore nothing", which is a different instruction from not setting
    /// it at all — falling back to the default here would ignore files the user told us not to.
    /// </summary>
    [Test]
    public async Task An_explicitly_empty_setting_ignores_nothing()
    {
        var patterns = Parse("[miscellany]", "global-ignores =");

        await Assert.That(Join(patterns)).IsEqualTo("");
    }

    [Test]
    public async Task An_empty_file_leaves_the_default_in_place()
    {
        await Assert.That(Parse()).IsEqualTo(GlobalIgnoreConfiguration.SubversionDefault);
    }

    [Test]
    public async Task Whitespace_around_the_setting_name_is_not_part_of_it()
    {
        var patterns = Parse("[miscellany]", "global-ignores   =   *.bak");

        await Assert.That(Join(patterns)).IsEqualTo("*.bak");
    }

    [Test]
    public async Task A_line_without_a_separator_is_not_a_setting()
    {
        var patterns = Parse("[miscellany]", "global-ignores", "  *.never");

        await Assert.That(patterns).IsEqualTo(GlobalIgnoreConfiguration.SubversionDefault);
    }

    [Test]
    public async Task A_differently_named_setting_is_not_this_one()
    {
        var patterns = Parse("[miscellany]", "global-ignores-extra = *.bak");

        await Assert.That(patterns).IsEqualTo(GlobalIgnoreConfiguration.SubversionDefault);
    }

    /// <summary>
    /// Each of these was confirmed one file at a time against `svn status` on a stock box, which
    /// is also how Thumbs.db was found *not* to be ignored despite being widely assumed to be.
    /// </summary>
    [Test]
    [Arguments("t.o", true)]
    [Arguments("t.lo", true)]
    [Arguments("t.la", true)]
    [Arguments("t.al", true)]
    [Arguments(".libs", true)]
    [Arguments("t.so", true)]
    [Arguments("t.so.6", true)]
    [Arguments("t.a", true)]
    [Arguments("t.pyc", true)]
    [Arguments("t.pyo", true)]
    [Arguments("__pycache__", true)]
    [Arguments("t.rej", true)]
    [Arguments("backup~", true)]
    [Arguments("#emacs#", true)]
    [Arguments(".#lock", true)]
    [Arguments(".t.swp", true)]
    [Arguments(".DS_Store", true)]
    [Arguments("Thumbs.db", false)]
    [Arguments("thumbs.db", false)]
    [Arguments("keep.txt", false)]
    public async Task The_default_list_ignores_what_svn_ignores(string name, bool expected)
    {
        var ignored = GlobalIgnoreConfiguration.SubversionDefault.Any(pattern =>
            SvnGlob.Matches(pattern, name)
        );

        await Assert.That(ignored).IsEqualTo(expected);
    }

    private static IReadOnlyList<string> Parse(params string[] lines) =>
        GlobalIgnoreConfiguration.Parse(lines);

    private static string Join(IReadOnlyList<string> patterns) => string.Join('|', patterns);
}
