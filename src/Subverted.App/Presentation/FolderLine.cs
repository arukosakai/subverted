namespace Subverted.App.Presentation;

/// <summary>One folder in the directory pane, with how many changes sit anywhere below it.</summary>
/// <param name="RelPath">Slash-separated and relative to the root; empty for the root itself.</param>
/// <param name="Name">The last segment; for the root, the working copy's own folder name.</param>
/// <param name="Depth">How many segments deep: the root is 0.</param>
/// <param name="Count">Changes this folder holds, at any depth — what choosing it would show.</param>
public sealed record FolderLine(string RelPath, string Name, int Depth, int Count)
{
    public bool IsRoot => RelPath.Length == 0;

    /// <summary>What a screen reader says for the line: the folder and how many changes it holds.</summary>
    public string AutomationName =>
        Count == 1
            ? $"{Name}, 1 change"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{Name}, {Count:N0} changes"
            );
}
