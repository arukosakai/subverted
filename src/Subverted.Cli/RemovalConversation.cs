namespace Subverted.Cli;

/// <summary>
/// The exchange between reading what a removal would take and sending it: refuse, stop, or ask.
/// Takes its terminal as dependencies so every answer is a test rather than something only a person
/// at a console could find out.
/// </summary>
/// <param name="prompt">Where the question is asked and answered.</param>
/// <param name="errors">Where refusals go, so a script sees them on stderr.</param>
/// <param name="show">Prints the preview's lines; the console's pager, when there is one.</param>
public sealed class RemovalConversation(
    IPrompt prompt,
    TextWriter errors,
    Action<IReadOnlyList<string>> show
)
{
    /// <param name="preview">What the removal would take, read fresh from the daemon.</param>
    /// <param name="alreadyConfirmed"><c>--yes</c> was given. It never overrides a refusal.</param>
    /// <param name="inputRedirected">
    /// The platform's answer to whether anyone can type a reply. A pipe cannot, and defaulting to
    /// yes for one is how a script takes a studio's assets off disk.
    /// </param>
    /// <returns>
    /// <c>null</c> to send the removal, or the exit code to return instead. A refusal is
    /// <see cref="ExitCode.UserError"/>; declining, or nothing to remove, is
    /// <see cref="ExitCode.Success"/>, since nothing was removed and that is what was asked for.
    /// </returns>
    public int? Confirm(RemovalPreview preview, bool alreadyConfirmed, bool inputRedirected)
    {
        if (preview.Refusals.Count > 0)
        {
            foreach (var refusal in preview.Refusals)
            {
                errors.WriteLine($"sv: {refusal}");
            }

            errors.WriteLine("sv: nothing removed");
            return ExitCode.UserError;
        }

        if (preview.Count == 0)
        {
            prompt.WriteLine("nothing to remove");
            return ExitCode.Success;
        }

        if (alreadyConfirmed)
        {
            return null;
        }

        show(preview.Lines);

        if (inputRedirected)
        {
            errors.WriteLine(
                $"sv: {preview.Count} node(s) above would be removed. "
                    + "Re-run with --yes to confirm; there is no terminal here to ask in."
            );
            return ExitCode.UserError;
        }

        prompt.Write($"remove {preview.Count} node(s)? [y/N] ");
        if (Confirmation.IsYes(prompt.ReadLine()))
        {
            return null;
        }

        prompt.WriteLine("nothing removed");
        return ExitCode.Success;
    }
}
