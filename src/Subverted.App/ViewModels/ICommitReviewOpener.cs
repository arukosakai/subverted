namespace Subverted.App.ViewModels;

/// <summary>Shows a commit review in front of the window that asked, and nothing else until it closes.</summary>
public interface ICommitReviewOpener
{
    /// <returns>
    /// Completes once the review is gone — because it asked to close, or the person closed it.
    /// </returns>
    Task OpenAsync(CommitReviewViewModel review);
}
