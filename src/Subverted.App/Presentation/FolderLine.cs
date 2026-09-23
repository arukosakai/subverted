namespace Subverted.App.Presentation;

/// <summary>One folder in the directory pane, with how many changes sit anywhere below it.</summary>
/// <param name="RelPath">Slash-separated and relative to the root; empty for the root itself.</param>
/// <param name="Name">The last segment; empty for the root, which the view names after the working copy.</param>
/// <param name="Depth">How many segments deep: the root is 0.</param>
/// <param name="Count">Changes this folder holds, at any depth — what choosing it would show.</param>
public sealed record FolderLine(string RelPath, string Name, int Depth, int Count)
{
    public bool IsRoot => RelPath.Length == 0;
}
