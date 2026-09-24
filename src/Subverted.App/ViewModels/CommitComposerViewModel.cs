using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// The box beneath the list: the message, the button that sends what is ticked, and what the last
/// attempt left behind. One commit is in flight at a time.
/// </summary>
/// <param name="committed">
/// Told the rows a commit sent once it reached a revision, so their ticks can go.
/// </param>
/// <param name="review">
/// Opens the review window on what would be sent; completes once that window is gone.
/// </param>
/// <remarks>Call it from the UI thread: its answers come back on the context that asked.</remarks>
public sealed partial class CommitComposerViewModel(
    IWorkingCopyCommit commits,
    Action<IReadOnlyList<string>> committed,
    Func<Task> review
) : ObservableObject
{
    private string _root = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand), nameof(ReviewCommand))]
    public partial bool IsCommitting { get; private set; }

    /// <summary>What the last commit came to; <c>null</c> before the first and once dismissed.</summary>
    [ObservableProperty]
    public partial Notice? Notice { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand), nameof(ReviewCommand))]
    public partial TickedSelection Selection { get; private set; } = TickedSelection.Nothing;

    public string ButtonText => CommitButtonText.For(Selection.Sent.Count);

    /// <summary>What a commit would send now.</summary>
    /// <param name="root">The working-copy root the selection's paths are relative to.</param>
    public void Offer(TickedSelection selection, string root)
    {
        _root = root;
        Selection = selection;
    }

    /// <summary>Raised once per commit, whatever it came to; an output log records these.</summary>
    public event Action<CommitAttempt>? Attempted;

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        var sending = Selection;
        var message = Message;
        var relPaths = sending.RelPaths;
        IsCommitting = true;
        Notice = null;
        CommitAttempt attempt;
        try
        {
            var response = await commits.CommitAsync(
                [.. relPaths.Select(relPath => DiffTarget.PathOf(_root, relPath))],
                message,
                cancellationToken
            );
            attempt = new CommitAttempt(relPaths, message, CommitNotices.For(response), response);
        }
        catch (DaemonUnreachableException unreachable)
        {
            attempt = new CommitAttempt(
                relPaths,
                message,
                CommitNotices.Unreachable(unreachable.Message),
                null
            );
        }
        finally
        {
            IsCommitting = false;
        }

        Notice = attempt.Notice;
        if (attempt.Answer is CommitSelectionResponse { Revision: not null })
        {
            Message = "";
            committed([.. sending.Sent.Select(row => row.RelPath)]);
        }

        Attempted?.Invoke(attempt);
    }

    private bool CanCommit() =>
        !IsCommitting && Selection.Sent.Count > 0 && !string.IsNullOrWhiteSpace(Message);

    /// <summary>Needs no message yet: the review window is somewhere to write it.</summary>
    [RelayCommand(CanExecute = nameof(CanReview))]
    private Task ReviewAsync() => review();

    private bool CanReview() => !IsCommitting && Selection.Sent.Count > 0;

    [RelayCommand]
    private void DismissNotice() => Notice = null;
}
