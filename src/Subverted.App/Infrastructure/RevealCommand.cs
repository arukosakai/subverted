namespace Subverted.App.Infrastructure;

/// <summary>The process that shows a path in a file manager reached from the command line.</summary>
/// <param name="Arguments">Passed one by one, so a path with spaces needs no quoting of ours.</param>
public sealed record RevealCommand(string FileName, IReadOnlyList<string> Arguments)
{
    /// <param name="path">Absolute, and present on disk.</param>
    public static RevealCommand Finder(string path) => new("open", ["-R", path]);

    /// <summary><c>xdg-open</c> cannot select, so a file's folder is the nearest thing to showing it.</summary>
    /// <param name="path">Absolute, and present on disk.</param>
    public static RevealCommand FreeDesktop(string path, bool isDirectory) =>
        new("xdg-open", [isDirectory ? path : Path.GetDirectoryName(path)!]);
}
