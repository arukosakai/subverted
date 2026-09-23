namespace Subverted.Svn.Tests;

public sealed class Utf8ConsoleRuleTests
{
    private const uint CentralEuropeanOem = 852;

    [Test]
    public async Task Outside_windows_nothing_is_done_even_without_a_console()
    {
        await Assert.That(Utf8ConsoleRule.StepFor(false, null)).IsEqualTo(Utf8ConsoleStep.Nothing);
    }

    [Test]
    public async Task Outside_windows_nothing_is_done_whatever_the_code_pages_say()
    {
        var oem = new ConsoleCodePages(CentralEuropeanOem, CentralEuropeanOem);

        await Assert.That(Utf8ConsoleRule.StepFor(false, oem)).IsEqualTo(Utf8ConsoleStep.Nothing);
    }

    /// <summary>
    /// Without one, every svn started gets a console of its own in the OEM code page — and a
    /// window on the desktop with it.
    /// </summary>
    [Test]
    public async Task On_windows_a_process_with_no_console_gets_one_without_a_window()
    {
        await Assert
            .That(Utf8ConsoleRule.StepFor(true, null))
            .IsEqualTo(Utf8ConsoleStep.AllocateWindowlessConsole);
    }

    [Test]
    public async Task On_windows_a_console_already_in_utf8_both_ways_is_left_alone()
    {
        var utf8 = new ConsoleCodePages(ConsoleCodePages.Utf8, ConsoleCodePages.Utf8);

        await Assert.That(Utf8ConsoleRule.StepFor(true, utf8)).IsEqualTo(Utf8ConsoleStep.Nothing);
    }

    [Test]
    [Arguments(CentralEuropeanOem, CentralEuropeanOem)]
    [Arguments(ConsoleCodePages.Utf8, CentralEuropeanOem)]
    [Arguments(CentralEuropeanOem, ConsoleCodePages.Utf8)]
    public async Task On_windows_a_console_not_utf8_both_ways_is_switched(uint output, uint input)
    {
        await Assert
            .That(Utf8ConsoleRule.StepFor(true, new ConsoleCodePages(output, input)))
            .IsEqualTo(Utf8ConsoleStep.SwitchCodePages);
    }
}
