namespace Subverted.Cli;

/// <summary>
/// Whether a typed answer means yes. Pure, and deliberately narrow: anything that is not an
/// explicit yes is a no, because the prompt this answers is the one before work is destroyed.
/// </summary>
public static class Confirmation
{
    /// <param name="answer">What the user typed, or <c>null</c> at end of input.</param>
    public static bool IsYes(string? answer) =>
        answer?.Trim() is { } typed
        && (
            typed.Equals("y", StringComparison.OrdinalIgnoreCase)
            || typed.Equals("yes", StringComparison.OrdinalIgnoreCase)
        );
}
