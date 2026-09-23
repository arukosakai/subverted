using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>What the pane says when SVN has no diff for a listed row, in terms of why it is listed.</summary>
public static class EmptyDiffMessage
{
    public static string For(ChangeRow row) =>
        (row.Badge.Tone, row.IsCopied) switch
        {
            (ChangeTone.Unversioned, _) =>
                "Not under version control yet, so SVN has nothing to compare it with.",
            (ChangeTone.Missing, _) =>
                "Missing from disk, so there is nothing here to compare with the committed version.",
            (ChangeTone.Added, true) =>
                "Copied or moved without edits since, so it matches where it came from.",
            (ChangeTone.Added, false) => "Added, with no content of its own to show.",
            _ => "SVN reports no differences here.",
        };
}
