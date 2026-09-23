namespace Subverted.Cli.Tests;

/// <summary>
/// The answer to the only prompt in <c>sv</c> that stands in front of destroyed work. Inverting any
/// line here turns a hesitation into a revert.
/// </summary>
public sealed class ConfirmationTests
{
    [Test]
    [Arguments("y")]
    [Arguments("Y")]
    [Arguments("yes")]
    [Arguments("YES")]
    [Arguments("  y  ")]
    public async Task An_explicit_yes_is_a_yes(string answer)
    {
        await Assert.That(Confirmation.IsYes(answer)).IsTrue();
    }

    [Test]
    [Arguments("n")]
    [Arguments("no")]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("ye")]
    [Arguments("yep")]
    [Arguments("yes please")]
    public async Task Anything_that_is_not_an_explicit_yes_is_a_no(string answer)
    {
        await Assert.That(Confirmation.IsYes(answer)).IsFalse();
    }

    /// <summary>
    /// End of input — a closed pipe, or Ctrl+D. Reading that as consent is how an unattended
    /// terminal reverts a working copy.
    /// </summary>
    [Test]
    public async Task No_answer_at_all_is_a_no()
    {
        await Assert.That(Confirmation.IsYes(null)).IsFalse();
    }
}
