namespace Subverted.Cli.Tests;

public sealed class DiffPaletteTests
{
    [Test]
    [Arguments(DiffLine.FileHeader)]
    [Arguments(DiffLine.HunkHeader)]
    [Arguments(DiffLine.Added)]
    [Arguments(DiffLine.Removed)]
    [Arguments(DiffLine.Context)]
    public async Task Plain_leaves_every_kind_of_line_exactly_as_it_was(DiffLine kind)
    {
        await Assert.That(DiffPalette.Plain("+three", kind)).IsEqualTo("+three");
    }

    [Test]
    [Arguments(DiffLine.FileHeader, "\e[1m")]
    [Arguments(DiffLine.HunkHeader, "\e[36m")]
    [Arguments(DiffLine.Added, "\e[32m")]
    [Arguments(DiffLine.Removed, "\e[31m")]
    public async Task Each_coloured_kind_is_wrapped_in_its_own_escape_and_reset(
        DiffLine kind,
        string escape
    )
    {
        await Assert.That(DiffPalette.Ansi("x", kind)).IsEqualTo($"{escape}x\e[0m");
    }

    /// <summary>
    /// Context is most of a diff. Colouring it would leave nothing for the changed lines to stand
    /// out against, which is the whole job.
    /// </summary>
    [Test]
    public async Task Context_is_left_the_colour_of_the_terminal()
    {
        await Assert.That(DiffPalette.Ansi(" one", DiffLine.Context)).IsEqualTo(" one");
    }
}
