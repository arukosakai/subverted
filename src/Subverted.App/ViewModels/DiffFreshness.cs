using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>
/// Whether a status refresh makes the diff on screen out of date. The one place that decides it,
/// so a finer signal than the row itself — a size or mtime on the entry — is a change here alone.
/// </summary>
public static class DiffFreshness
{
    /// <param name="shownFor">The row the diff on screen was asked for.</param>
    /// <param name="listed">The same path's row in the listing that just arrived.</param>
    public static bool NeedsRefetch(ChangeRow shownFor, ChangeRow listed) => shownFor != listed;
}
