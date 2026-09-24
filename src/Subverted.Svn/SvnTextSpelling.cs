namespace Subverted.Svn;

/// <summary>Picks the <see cref="ISvnTextSpelling"/> for the platform svn runs on.</summary>
public static class SvnTextSpelling
{
    public static ISvnTextSpelling ForThisMachine() => For(OperatingSystem.IsWindows());

    /// <param name="isWindows">Taken as an argument so both answers are tested on either platform.</param>
    internal static ISvnTextSpelling For(bool isWindows) =>
        isWindows ? new AnsiCodePageSpelling() : new Utf8Spelling();
}
