namespace Subverted.Cli.Tests;

public sealed class LogPaletteTests
{
    [Test]
    [Arguments(LogLine.Heading)]
    [Arguments(LogLine.Path)]
    [Arguments(LogLine.Message)]
    public async Task Plain_leaves_every_kind_of_line_exactly_as_it_was(LogLine kind)
    {
        await Assert.That(LogPalette.Plain("r42", kind)).IsEqualTo("r42");
    }

    [Test]
    [Arguments(LogLine.Heading, "\e[33m")]
    [Arguments(LogLine.Path, "\e[90m")]
    public async Task A_heading_and_a_path_each_get_a_colour_of_their_own(
        LogLine kind,
        string escape
    )
    {
        await Assert.That(LogPalette.Ansi("x", kind)).IsEqualTo($"{escape}x\e[0m");
    }

    /// <summary>The message is the committer's own words, and recolouring them says nothing.</summary>
    [Test]
    public async Task A_message_is_left_the_colour_of_the_terminal()
    {
        await Assert
            .That(LogPalette.Ansi("    re-export", LogLine.Message))
            .IsEqualTo("    re-export");
    }
}
