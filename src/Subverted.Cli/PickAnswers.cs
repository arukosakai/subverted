namespace Subverted.Cli;

/// <summary>
/// Reads one answer to the picker's prompt. Anything unrecognised is <see langword="null"/> rather
/// than a default: this prompt decides what goes to the server, and a typo must re-ask rather than
/// guess.
/// </summary>
public static class PickAnswers
{
    /// <param name="typed">
    /// What the user typed, or <see langword="null"/> at end of input — which is not an answer, and
    /// the only safe reading of it is to stop without committing.
    /// </param>
    public static PickAnswer? Of(string? typed)
    {
        if (typed is null)
        {
            return PickAnswer.Quit;
        }

        return typed.Trim().ToLowerInvariant() switch
        {
            "y" or "yes" => PickAnswer.Send,
            "n" or "no" => PickAnswer.Skip,
            "a" or "all" => PickAnswer.SendRest,
            "d" or "done" => PickAnswer.SkipRest,
            "q" or "quit" => PickAnswer.Quit,
            "?" or "h" or "help" => PickAnswer.Explain,
            _ => null,
        };
    }
}
