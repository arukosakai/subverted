namespace Subverted.Svn;

/// <summary>
/// Decides <see cref="Utf8ConsoleStep"/> from what the process can see of its console. Split from
/// <see cref="SvnConsole"/> so both platforms' answers are tested on either one.
/// </summary>
internal static class Utf8ConsoleRule
{
    /// <param name="isWindows">Elsewhere svn writes in the locale's charset, which <see cref="SvnLocale"/> settles.</param>
    /// <param name="console"><c>null</c> when the process has no console at all.</param>
    public static Utf8ConsoleStep StepFor(bool isWindows, ConsoleCodePages? console)
    {
        if (!isWindows)
        {
            return Utf8ConsoleStep.Nothing;
        }

        if (console is not { } codePages)
        {
            return Utf8ConsoleStep.AllocateWindowlessConsole;
        }

        return codePages.AreBothUtf8 ? Utf8ConsoleStep.Nothing : Utf8ConsoleStep.SwitchCodePages;
    }
}
