namespace Subverted.Svn;

/// <summary>
/// The <c>LC_ALL</c> every svn runs under: English, so messages and warning text parse the same on
/// every machine, and off Windows a UTF-8 charset, because under plain C svn cannot read a
/// directory holding a non-ASCII name at all (E000022).
/// </summary>
internal static class SvnLocale
{
    /// <param name="isWindows">There svn's charset comes from code pages, not the locale — see D33.</param>
    /// <param name="isMacOS">macOS is not guaranteed a C.UTF-8 locale; en_US.UTF-8 it always has.</param>
    public static string LcAllFor(bool isWindows, bool isMacOS)
    {
        if (isWindows)
        {
            return "C";
        }

        return isMacOS ? "en_US.UTF-8" : "C.UTF-8";
    }
}
