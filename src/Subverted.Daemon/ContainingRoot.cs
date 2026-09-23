namespace Subverted.Daemon;

/// <summary>
/// Which of a set of working-copy roots contains a path. Pure string work on purpose: this runs on
/// the warm path, where going to the disk to answer costs more than the answer is worth.
/// </summary>
public static class ContainingRoot
{
    /// <summary>
    /// Windows compares paths without case and everything else compares them with it. Getting this
    /// backwards opens a second index for a working copy that is already held.
    /// </summary>
    public static StringComparison PlatformComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <param name="path">An absolute path. It does not have to exist.</param>
    /// <returns>
    /// The longest root containing <paramref name="path"/>, so a checkout nested inside another one
    /// wins over its parent; <c>null</c> when none contains it.
    /// </returns>
    public static string? Of(string path, IEnumerable<string> roots, StringComparison comparison)
    {
        string? longest = null;
        foreach (var root in roots)
        {
            if (Contains(root, path, comparison) && root.Length > (longest?.Length ?? -1))
            {
                longest = root;
            }
        }

        return longest;
    }

    private static bool Contains(string root, string path, StringComparison comparison)
    {
        // A root of "/" trims to nothing, and that is the right answer rather than a case to guard:
        // every absolute path starts with the empty prefix and then with a separator, which is
        // exactly what "contained by the filesystem root" means. A relative path does not.
        var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!path.StartsWith(trimmed, comparison))
        {
            return false;
        }

        // `/wc` must not claim `/wc2`: past the root there has to be a separator, or nothing left.
        return path.Length == trimmed.Length || IsSeparator(path[trimmed.Length]);
    }

    private static bool IsSeparator(char character) =>
        character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar;
}
