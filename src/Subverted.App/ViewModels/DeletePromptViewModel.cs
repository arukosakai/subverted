using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// The question asked before a delete, with the exact list of what it reaches read afresh from the
/// daemon, and what the delete came to. Nothing is sent until the person confirms that list, and
/// not then either if the list read again at that moment differs from it.
/// </summary>
/// <remarks>Call it from the UI thread: its answers come back on the context that asked.</remarks>
public sealed partial class DeletePromptViewModel(IWorkingCopyDeletion deletions) : ObservableObject
{
    /// <summary>Every path here is the daemon's own spelling within one listing.</summary>
    private const StringComparison Ordinal = StringComparison.Ordinal;

    private string _root = "";
    private bool _isLooking;

    /// <summary>The question on screen; <c>null</c> when none is being asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial DeletionPreview? Pending { get; private set; }

    public bool IsAsking => Pending is not null;

    /// <summary>The list on screen is not the one first shown: the working copy changed under the question.</summary>
    [ObservableProperty]
    public partial bool HasChangedSinceAsked { get; private set; }

    /// <summary>A confirmed delete is being checked or sent; the question on screen is the one it is for.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(CancelCommand))]
    public partial bool IsDeleting { get; private set; }

    /// <summary>What the last delete came to, or why none was asked; <c>null</c> before the first and once dismissed.</summary>
    [ObservableProperty]
    public partial Notice? Notice { get; private set; }

    /// <summary>Raised once per delete sent, whatever it came to; an output log records these.</summary>
    public event Action<DeleteAttempt>? Attempted;

    /// <summary>
    /// Reads what deleting the target would reach and puts the question, or says why there is none.
    /// Ignored while a delete runs or another question is being read, so one runs at a time.
    /// </summary>
    /// <param name="root">The working-copy root the target is relative to.</param>
    /// <param name="target">The picked line's path, relative to the root.</param>
    public async Task AskAsync(string root, string target, CancellationToken cancellationToken)
    {
        if (IsDeleting || _isLooking)
        {
            return;
        }

        _isLooking = true;
        try
        {
            _root = root;
            Notice = null;
            HasChangedSinceAsked = false;
            var (question, instead) = await LookAsync(target, cancellationToken);
            Pending = question;
            Notice = instead;
        }
        finally
        {
            _isLooking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken cancellationToken)
    {
        var asked = Pending!;
        IsDeleting = true;
        DeleteAttempt attempt;
        try
        {
            var (current, instead) = await LookAsync(asked.Target, cancellationToken);
            if (current is null)
            {
                PutAway();
                Notice = instead;
                return;
            }

            if (!current.IsSameQuestionAs(asked))
            {
                HasChangedSinceAsked = true;
                Pending = current;
                return;
            }

            attempt = await SendAsync(asked.Target, cancellationToken);
            PutAway();
        }
        finally
        {
            IsDeleting = false;
        }

        Notice = attempt.Notice;
        Attempted?.Invoke(attempt);
    }

    private bool CanConfirm() => Pending is not null && !IsDeleting;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => PutAway();

    private bool CanCancel() => !IsDeleting;

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    private void PutAway()
    {
        HasChangedSinceAsked = false;
        Pending = null;
    }

    private async Task<(DeletionPreview? Question, Notice? Instead)> LookAsync(
        string target,
        CancellationToken cancellationToken
    )
    {
        DaemonResponse response;
        try
        {
            response = await deletions.ListAsync(
                DiffTarget.PathOf(_root, target),
                cancellationToken
            );
        }
        catch (DaemonUnreachableException unreachable)
        {
            return (null, DeleteNotices.CouldNotLook(target, unreachable.Message));
        }

        return response switch
        {
            StatusResponse listing => QuestionFrom(target, listing),
            ErrorResponse error => (null, DeleteNotices.CouldNotLook(target, error.Message)),
            _ => (
                null,
                DeleteNotices.CouldNotLook(
                    target,
                    $"The daemon answered with {response.GetType().Name}, which is not a listing."
                )
            ),
        };
    }

    private static (DeletionPreview? Question, Notice? Instead) QuestionFrom(
        string target,
        StatusResponse listing
    )
    {
        var node = listing.Entries.FirstOrDefault(entry => entry.RelPath == target);
        if (node is null)
        {
            return (null, DeleteNotices.NothingLeft(target));
        }

        return DeletionOffer.RefusalFor(node, listing.Entries, Ordinal) is { } reason
            ? (null, DeleteNotices.Refused(target, reason))
            : (DeletionPreview.Of(target, listing.Entries, Ordinal), null);
    }

    private async Task<DeleteAttempt> SendAsync(string target, CancellationToken cancellationToken)
    {
        try
        {
            var response = await deletions.DeleteAsync(
                DiffTarget.PathOf(_root, target),
                cancellationToken
            );
            return new DeleteAttempt(target, DeleteNotices.For(target, response), response);
        }
        catch (DaemonUnreachableException unreachable)
        {
            return new DeleteAttempt(target, DeleteNotices.Unreachable(unreachable.Message), null);
        }
    }
}
