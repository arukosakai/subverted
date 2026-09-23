namespace Subverted.Cli;

/// <summary>
/// As much of a terminal as a question-and-answer loop needs. Named for the consumer rather than
/// for the console, so the loop that decides what gets committed is driven by a test rather than
/// only by a person sitting at one.
/// </summary>
public interface IPrompt
{
    /// <summary>Writes without ending the line, so an answer is typed after it.</summary>
    void Write(string text);

    void WriteLine(string text);

    /// <returns>The line that was typed, or <see langword="null"/> at end of input.</returns>
    string? ReadLine();
}
