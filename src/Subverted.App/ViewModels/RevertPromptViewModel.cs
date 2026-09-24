using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>
/// The question asked before a revert, with the exact list of what it touches, and what the revert
/// came to. Nothing is sent until the person confirms that list.
/// </summary>
/// <remarks>Call it from the UI thread: its answers come back on the context that asked.</remarks>
public sealed partial class RevertPromptViewModel(IWorkingCopyRevert reverts) : ObservableObject
{
    private string _root = "";
    private Func<RevertConfirmation>? _relist;

    /// <summary>The question on screen; <c>null</c> when none is being asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial RevertConfirmation? Pending { get; private set; }

    public bool IsAsking => Pending is not null;

    /// <summary>The list on screen is not the one first shown: the listing changed under the question.</summary>
    [ObservableProperty]
    public partial bool HasChangedSinceAsked { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(CancelCommand))]
    public partial bool IsReverting { get; private set; }

    /// <summary>What the last revert came to; <c>null</c> before the first and once dismissed.</summary>
    [ObservableProperty]
    public partial Notice? Notice { get; private set; }

    /// <summary>Puts the question. Ignored while a revert is running, so its list stays the one sent.</summary>
    /// <param name="root">The working-copy root the confirmation's paths are relative to.</param>
    /// <param name="relist">
    /// The same question against the listing as it stands when confirmed. A revert reaches whatever
    /// is under its target by then, so a list that differs is shown again rather than sent.
    /// </param>
    public void Ask(RevertConfirmation confirmation, string root, Func<RevertConfirmation> relist)
    {
        if (IsReverting)
        {
            return;
        }

        _root = root;
        _relist = relist;
        Notice = null;
        HasChangedSinceAsked = false;
        Pending = confirmation;
    }

    /// <summary>Raised once per confirmed revert, whatever it came to; an output log records these.</summary>
    public event Action<RevertAttempt>? Attempted;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken cancellationToken)
    {
        var target = Pending!.Target;
        var current = _relist!();
        if (current.Lines.Count == 0)
        {
            Pending = null;
            Notice = RevertNotices.NothingLeft(target);
            return;
        }

        if (!current.Lines.SequenceEqual(Pending.Lines))
        {
            HasChangedSinceAsked = true;
            Pending = current;
            return;
        }

        IsReverting = true;
        RevertAttempt attempt;
        try
        {
            var response = await reverts.RevertAsync(
                DiffTarget.PathOf(_root, target),
                cancellationToken
            );
            attempt = new RevertAttempt(target, RevertNotices.For(target, response), response);
        }
        catch (DaemonUnreachableException unreachable)
        {
            attempt = new RevertAttempt(
                target,
                RevertNotices.Unreachable(unreachable.Message),
                null
            );
        }
        finally
        {
            IsReverting = false;
            HasChangedSinceAsked = false;
            Pending = null;
        }

        Notice = attempt.Notice;
        Attempted?.Invoke(attempt);
    }

    private bool CanConfirm() => Pending is not null && !IsReverting;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        HasChangedSinceAsked = false;
        Pending = null;
    }

    private bool CanCancel() => !IsReverting;

    [RelayCommand]
    private void DismissNotice() => Notice = null;
}
