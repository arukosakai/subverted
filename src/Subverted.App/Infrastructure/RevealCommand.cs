namespace Subverted.App.Infrastructure;

/// <summary>The process that shows a path selected in the platform's file manager.</summary>
/// <param name="Arguments">Passed one by one, so a path with spaces needs no quoting of ours.</param>
public sealed record RevealCommand(string FileName, IReadOnlyList<string> Arguments)
{
    /// <param name="path">Absolute, and present on disk.</param>
    /// <param name="fileManager">Which one this platform has; taken as an argument so every side is a test.</param>
    /// <param name="isDirectory">Only a file manager that cannot select opens the folder itself.</param>
    /// <remarks>
    /// Explorer parses its own command line: <c>/select,</c> and the path must be separate
    /// arguments, since a path glued to the comma is not re-quoted when it holds a space.
    /// </remarks>
    public static RevealCommand For(string path, FileManager fileManager, bool isDirectory) =>
        fileManager switch
        {
            FileManager.Explorer => new("explorer.exe", ["/select,", path]),
            FileManager.Finder => new("open", ["-R", path]),
            _ => new("xdg-open", [isDirectory ? path : Path.GetDirectoryName(path)!]),
        };
}
