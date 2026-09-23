namespace Subverted.Svn;

/// <summary>
/// SVN's per-path notification text, with paths spelled the way the rest of Subverted spells them.
/// </summary>
/// <remarks>
/// SVN prints the platform's own separator whatever it was given — <c>A  src\b.txt</c> on Windows,
/// and <c>svn status</c> does the same — while every <c>RelPath</c> Subverted reports is
/// slash-separated. Left alone, <c>sv add</c> and <c>sv st</c> name one file two ways.
/// </remarks>
public static class SvnNotification
{
    /// <param name="backslashIsOnlyASeparator">
    /// Whether this platform reserves <c>\</c>. Taken as an argument rather than read, so both
    /// answers are covered by tests on either operating system: on POSIX a backslash is a legal
    /// character in a filename, and rewriting it would name a different file.
    /// </param>
    public static string Spelled(string notifications, bool backslashIsOnlyASeparator) =>
        backslashIsOnlyASeparator ? notifications.Replace('\\', '/') : notifications;
}
