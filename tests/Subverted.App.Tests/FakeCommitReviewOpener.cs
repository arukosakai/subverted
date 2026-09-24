using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

/// <summary>
/// Keeps each review open, as a modal window would, until it asks to close — or until the test
/// closes it the way the person clicking the window's own close button would.
/// </summary>
internal sealed class FakeCommitReviewOpener : ICommitReviewOpener
{
    private TaskCompletionSource? _showing;

    public List<CommitReviewViewModel> Opened { get; } = [];

    public bool IsOpen => _showing is { Task.IsCompleted: false };

    /// <summary>How many times the open review asked to close; more than once is a bug.</summary>
    public int CloseRequests { get; private set; }

    /// <summary>The last review opened.</summary>
    public CommitReviewViewModel Review => Opened[^1];

    public Task OpenAsync(CommitReviewViewModel review)
    {
        Opened.Add(review);
        var showing = new TaskCompletionSource();
        _showing = showing;
        review.CloseRequested += () =>
        {
            CloseRequests++;
            showing.TrySetResult();
        };
        return showing.Task;
    }

    public void CloseFromOutside() => _showing?.TrySetResult();
}
