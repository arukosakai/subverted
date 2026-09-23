namespace Subverted.Cli.Tests;

/// <summary>
/// What a keystroke at the picker's prompt means. The negative cases carry the weight: an answer
/// this cannot read has to come back as nothing so the question is asked again, because any default
/// either sends a file nobody approved or silently drops one they did.
/// </summary>
public sealed class PickAnswersTests
{
    [Test]
    [Arguments("y", PickAnswer.Send)]
    [Arguments("yes", PickAnswer.Send)]
    [Arguments("n", PickAnswer.Skip)]
    [Arguments("no", PickAnswer.Skip)]
    [Arguments("a", PickAnswer.SendRest)]
    [Arguments("all", PickAnswer.SendRest)]
    [Arguments("d", PickAnswer.SkipRest)]
    [Arguments("done", PickAnswer.SkipRest)]
    [Arguments("q", PickAnswer.Quit)]
    [Arguments("quit", PickAnswer.Quit)]
    [Arguments("?", PickAnswer.Explain)]
    [Arguments("h", PickAnswer.Explain)]
    [Arguments("help", PickAnswer.Explain)]
    public async Task Each_letter_means_what_the_prompt_said_it_means(
        string typed,
        PickAnswer expected
    )
    {
        await Assert.That(PickAnswers.Of(typed)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Y", PickAnswer.Send)]
    [Arguments("  n  ", PickAnswer.Skip)]
    [Arguments("ALL", PickAnswer.SendRest)]
    public async Task Case_and_surrounding_space_are_not_part_of_the_answer(
        string typed,
        PickAnswer expected
    )
    {
        await Assert.That(PickAnswers.Of(typed)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("yep")]
    [Arguments("ye")]
    [Arguments("s")]
    [Arguments("y n")]
    public async Task An_answer_this_cannot_read_is_nothing_rather_than_a_default(string typed)
    {
        await Assert.That(PickAnswers.Of(typed)).IsNull();
    }

    /// <summary>
    /// End of input is not an answer, and the only safe reading of it is to stop. Returning null
    /// here would re-ask a question nobody is left to answer, forever.
    /// </summary>
    [Test]
    public async Task End_of_input_stops_the_walk_rather_than_asking_again()
    {
        await Assert.That(PickAnswers.Of(null)).IsEqualTo(PickAnswer.Quit);
    }
}
