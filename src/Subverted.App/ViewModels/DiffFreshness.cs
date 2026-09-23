using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>
/// Whether a status refresh makes the diff on screen out of date. The row is the signal: it
/// carries the file's size and write time, so a second save of an already-modified file changes it.
/// </summary>
public static class DiffFreshness
{
    /// <param name="shownFor">The row the diff on screen was asked for.</param>
    /// <param name="listed">The same path's row in the listing that just arrived.</param>
    public static bool NeedsRefetch(ChangeRow shownFor, ChangeRow listed) => shownFor != listed;
}
