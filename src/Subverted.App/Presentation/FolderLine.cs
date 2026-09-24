namespace Subverted.App.Presentation;

/// <summary>One folder in the directory pane, with how many changes sit anywhere below it.</summary>
/// <param name="RelPath">Slash-separated and relative to the root; empty for the root itself.</param>
/// <param name="Name">The last segment; for the root, the working copy's own folder name.</param>
/// <param name="Depth">How many segments deep: the root is 0.</param>
/// <param name="Count">Changes this folder holds, at any depth — what choosing it would show.</param>
/// <param name="Tone">The most urgent tone among those changes, which the line is drawn in.</param>
/// <param name="HasSubfolders">Whether another line of the tree sits under this one, so it can collapse.</param>
public sealed record FolderLine(
    string RelPath,
    string Name,
    int Depth,
    int Count,
    ChangeTone Tone,
    bool HasSubfolders
)
{
    /// <summary>Whether the lines under this one are hidden; only ever true with <see cref="HasSubfolders"/>.</summary>
    public bool IsCollapsed { get; init; }

    public bool IsRoot => RelPath.Length == 0;

    /// <summary>The folder one level up: empty for a top-level folder, <c>null</c> for the root.</summary>
    public string? Parent =>
        IsRoot ? null
        : RelPath.LastIndexOf('/') is var slash and >= 0 ? RelPath[..slash]
        : "";

    public bool CanCollapse => HasSubfolders && !IsCollapsed;

    public bool CanExpand => HasSubfolders && IsCollapsed;

    /// <summary>
    /// What a screen reader says for the line: the folder, how many changes it holds and, for one
    /// with folders under it, whether they are shown.
    /// </summary>
    public string AutomationName => $"{Name}, {CountText}{BranchText}";

    /// <summary>What a screen reader says for the chevron: what pressing it would do.</summary>
    public string ToggleName => IsCollapsed ? $"Expand {Name}" : $"Collapse {Name}";

    private string CountText =>
        Count == 1
            ? "1 change"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{Count:N0} changes"
            );

    private string BranchText =>
        !HasSubfolders ? ""
        : IsCollapsed ? ", collapsed"
        : ", expanded";
}
