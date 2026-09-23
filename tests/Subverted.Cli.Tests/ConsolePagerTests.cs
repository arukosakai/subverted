namespace Subverted.Cli.Tests;

/// <summary>
/// Off-by-one in a pager is the difference between seeing a line and never seeing it, so the
/// arithmetic is tested with the prompt injected rather than by staring at a terminal.
/// </summary>
public sealed class ConsolePagerTests
{
    [Test]
    public async Task Writing_emits_every_line_and_nothing_else()
    {
        var output = new StringWriter();

        ConsolePager.Write(output, ["one", "two"]);

        await Assert.That(Lines(output)).IsEquivalentTo(new[] { "one", "two" });
    }

    [Test]
    public async Task Writing_nothing_emits_nothing()
    {
        var output = new StringWriter();

        ConsolePager.Write(output, []);

        await Assert.That(output.ToString()).IsEmpty();
    }

    /// <summary>One line of the screen goes to the prompt, so a height of three shows two.</summary>
    [Test]
    public async Task A_screenful_is_one_line_shorter_than_the_terminal()
    {
        var output = new StringWriter();
        var prompts = 0;

        ConsolePager.Page(
            output,
            ["1", "2", "3", "4"],
            pageSize: 3,
            continues: () =>
            {
                prompts++;
                return true;
            }
        );

        await Assert.That(Lines(output)).IsEquivalentTo(new[] { "1", "2", "3", "4" });
        await Assert.That(prompts).IsEqualTo(1);
    }

    [Test]
    public async Task Quitting_at_the_prompt_stops_the_listing_there()
    {
        var output = new StringWriter();

        ConsolePager.Page(output, ["1", "2", "3", "4"], pageSize: 3, continues: () => false);

        await Assert.That(Lines(output)).IsEquivalentTo(new[] { "1", "2" });
    }

    /// <summary>
    /// The prompt only appears when there is another screenful behind it. Asking after the last
    /// line is the bug that makes a pager feel broken.
    /// </summary>
    [Test]
    public async Task Nothing_is_asked_when_the_listing_ends_on_a_screen_boundary()
    {
        var output = new StringWriter();
        var prompts = 0;

        ConsolePager.Page(
            output,
            ["1", "2"],
            pageSize: 3,
            continues: () =>
            {
                prompts++;
                return true;
            }
        );

        await Assert.That(Lines(output)).IsEquivalentTo(new[] { "1", "2" });
        await Assert.That(prompts).IsEqualTo(0);
    }

    [Test]
    public async Task A_listing_shorter_than_a_screen_is_never_paged()
    {
        var output = new StringWriter();
        var prompts = 0;

        ConsolePager.Page(
            output,
            ["1"],
            pageSize: 10,
            continues: () =>
            {
                prompts++;
                return true;
            }
        );

        await Assert.That(prompts).IsEqualTo(0);
    }

    /// <summary>
    /// A one-line terminal would otherwise give a screenful of zero lines and loop forever showing
    /// nothing. The floor is one line per screen.
    /// </summary>
    [Test]
    [Arguments(1)]
    [Arguments(0)]
    public async Task A_terminal_too_short_to_page_still_makes_progress(int pageSize)
    {
        var output = new StringWriter();

        ConsolePager.Page(output, ["1", "2"], pageSize, continues: () => true);

        await Assert.That(Lines(output)).IsEquivalentTo(new[] { "1", "2" });
    }

    [Test]
    public async Task Paging_nothing_asks_nothing_and_emits_nothing()
    {
        var output = new StringWriter();
        var prompts = 0;

        ConsolePager.Page(
            output,
            [],
            pageSize: 3,
            continues: () =>
            {
                prompts++;
                return true;
            }
        );

        await Assert.That(output.ToString()).IsEmpty();
        await Assert.That(prompts).IsEqualTo(0);
    }

    private static string[] Lines(StringWriter output) =>
        output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
