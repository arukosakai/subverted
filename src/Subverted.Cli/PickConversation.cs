namespace Subverted.Cli;

/// <summary>
/// Asks about each change until the walk is over. The only thing it decides is what to do with an
/// answer that is not one — everything else belongs to <see cref="ChangePicker"/>.
/// </summary>
public static class PickConversation
{
    /// <summary>
    /// Runs the walk to its end. An unreadable answer explains the letters and asks the same node
    /// again rather than moving on, because moving on would mean deciding it for the person.
    /// </summary>
    public static void Walk(ChangePicker picker, Paint paint, IPrompt prompt, int count)
    {
        prompt.WriteLine(PickReport.Opening(count));

        while (picker.Current is { } entry)
        {
            prompt.Write(PickReport.Question(entry, paint));
            var answer = PickAnswers.Of(prompt.ReadLine());

            if (answer is null or PickAnswer.Explain)
            {
                foreach (var line in PickReport.Explanation)
                {
                    prompt.WriteLine(line);
                }

                continue;
            }

            picker.Answer(answer.Value);
        }
    }
}
