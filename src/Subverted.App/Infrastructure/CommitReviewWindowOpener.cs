using Avalonia.Controls;
using Subverted.App.ViewModels;
using Subverted.App.Views;

namespace Subverted.App.Infrastructure;

/// <summary>Shows the review as a window modal to the one that asked.</summary>
/// <param name="owner">
/// Asked when the review opens rather than at construction: the window and its view model need
/// each other, and the window is the one that can be looked up later.
/// </param>
public sealed class CommitReviewWindowOpener(Func<Window?> owner) : ICommitReviewOpener
{
    public Task OpenAsync(CommitReviewViewModel review) =>
        owner() is { } parent
            ? new CommitReviewWindow(review).ShowDialog(parent)
            : Task.CompletedTask;
}
