namespace Subverted.App.ViewModels;

/// <summary>The absolute path a row names, which is what a <c>DiffRequest</c> and the disk want.</summary>
public static class DiffTarget
{
    /// <param name="root">The working-copy root, as the daemon reported it.</param>
    /// <param name="relPath">Slash-separated; empty for the root itself.</param>
    /// <remarks>
    /// The spelling of <paramref name="root"/> does not matter: the daemon respells every request
    /// path on arrival (D31). Only a slash is rewritten, to the platform's separator, so on Unix a
    /// name is never touched.
    /// </remarks>
    public static string PathOf(string root, string relPath) =>
        relPath.Length == 0
            ? root
            : Path.Join(root, relPath.Replace('/', Path.DirectorySeparatorChar));
}
