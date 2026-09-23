namespace Subverted.Core;

/// <summary>
/// Whether a node the daemon reported lies under a path the user named. Entries spell themselves
/// slash-separated and relative to the working-copy root, and the user types whatever their shell
/// gave them, so the two have to be brought into one vocabulary before they can be compared.
/// </summary>
public static class TargetCoverage
{
    /// <summary>
    /// An absolute path as the entries spell themselves: slash-separated, relative to the root, and
    /// empty for the root itself.
    /// </summary>
    public static string RelativeTo(string rootPath, string path)
    {
        var relative = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
        return relative == "." ? string.Empty : relative;
    }

    /// <param name="comparison">
    /// How this platform compares paths. Taken as an argument so both answers are covered by tests
    /// on either operating system.
    /// </param>
    /// <returns>True for the target itself and for everything beneath it.</returns>
    public static bool Covers(string target, string relPath, StringComparison comparison) =>
        // The root covers everything, including the root's own empty path — which a prefix test
        // spelled `target + "/"` would miss.
        target.Length == 0
        || relPath.Equals(target, comparison)
        || Below(target, relPath, comparison);

    /// <returns>True beneath the ancestor, and false for the ancestor itself.</returns>
    public static bool Below(string ancestor, string relPath, StringComparison comparison) =>
        ancestor.Length == 0 ? relPath.Length > 0 : relPath.StartsWith(ancestor + "/", comparison);
}
