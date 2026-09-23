namespace Subverted.Cli;

/// <summary>
/// Puts lines on the screen. Paging is done here rather than by piping to <c>less</c>, which a
/// stock Windows box does not have.
/// </summary>
public static class ConsolePager
{
    public static void Write(TextWriter output, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }
    }

    /// <param name="pageSize">Terminal height. One line of it goes to the prompt.</param>
    /// <param name="continues">
    /// Asked between screenfuls; false stops the listing. Injected so the paging arithmetic can be
    /// tested without a terminal attached.
    /// </param>
    public static void Page(
        TextWriter output,
        IReadOnlyList<string> lines,
        int pageSize,
        Func<bool> continues
    )
    {
        var perScreen = Math.Max(1, pageSize - 1);

        for (var start = 0; start < lines.Count; start += perScreen)
        {
            for (var index = start; index < Math.Min(start + perScreen, lines.Count); index++)
            {
                output.WriteLine(lines[index]);
            }

            // Nothing left to show, so nothing to ask about — the prompt only appears when there
            // really is another screenful behind it.
            if (start + perScreen >= lines.Count || !continues())
            {
                return;
            }
        }
    }
}
