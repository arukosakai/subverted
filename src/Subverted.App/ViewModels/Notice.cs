using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>What the view says after a write: a commit, a revert, an update, a resolve or a lock.</summary>
/// <param name="Headline">One line: what happened.</param>
/// <param name="Detail">SVN's or the daemon's own text, shown as it came; <c>null</c> when there is none.</param>
/// <param name="Hint">What that means for the working copy and what to do next; <c>null</c> when nothing.</param>
public sealed record Notice(NoticeKind Kind, string Headline, string? Detail, string? Hint)
{
    /// <summary>
    /// The colour it is drawn in, borrowed from the list's tones: done reads as added, marked but not
    /// sent as missing, refused or left for the person as a conflict, and not knowing as an edit.
    /// </summary>
    public ChangeTone Tone =>
        Kind switch
        {
            NoticeKind.Succeeded => ChangeTone.Added,
            NoticeKind.LeftMarked => ChangeTone.Missing,
            NoticeKind.NothingWritten or NoticeKind.NeedsAttention => ChangeTone.Conflict,
            _ => ChangeTone.Modified,
        };
}
