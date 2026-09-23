namespace Subverted.App.Presentation;

/// <summary>What the History diff pane says when SVN printed nothing for a changed path.</summary>
public static class RevisionDiffEmptyMessage
{
    /// <remarks>
    /// SVN compares a copy with its source, so a copy made without edits prints nothing — measured
    /// on 1.8.15, and the common case for a rename.
    /// </remarks>
    public static string For(ChangedPathRow path) =>
        path.CopiedFrom is { } source
            ? $"Copied from {source} without edits, so it matches where it came from."
            : "SVN shows no content or property changes for this path in this revision.";
}
