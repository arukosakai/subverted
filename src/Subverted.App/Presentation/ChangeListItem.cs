namespace Subverted.App.Presentation;

/// <summary>One line of the list as laid out: a change, or in the tree a folder holding changes.</summary>
/// <param name="RelPath">What the line is about: the change's path, or the folder's.</param>
/// <param name="Name">What the line reads first.</param>
/// <param name="Folder">Drawn faint after the name; empty where the indent already says it.</param>
/// <param name="Depth">How far the tree indents it; always 0 in the flat list.</param>
/// <param name="Row">The change on this line; <c>null</c> for a folder that is only there to hold others.</param>
public sealed record ChangeListItem(
    string RelPath,
    string Name,
    string Folder,
    int Depth,
    ChangeRow? Row
)
{
    public static ChangeListItem Flat(ChangeRow row) =>
        new(row.RelPath, row.Name, row.Folder, Depth: 0, row);

    /// <summary>
    /// Unique within one layout. A folder that only holds others gets a trailing slash, which no
    /// change's path has, so it cannot collide with a pinned directory's own line above the tree.
    /// </summary>
    public string Key => Row is null ? RelPath + "/" : RelPath;

    /// <summary>
    /// What a screen reader says for the line. The folder is always said, even where the tree's
    /// indent shows it, because an indent is not heard.
    /// </summary>
    public string AutomationName =>
        Row switch
        {
            null => $"{Name}, folder",
            { Folder.Length: 0 } => $"{Name}, {Row.Badge.Label}",
            _ => $"{Name} in {Row.Folder}, {Row.Badge.Label}",
        };
}
