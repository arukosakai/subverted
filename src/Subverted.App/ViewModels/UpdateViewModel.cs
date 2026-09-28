using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// The Update button, what the last update came to, and how much the next one would bring. Nothing
/// is asked first: an update takes no local change away, and what it cannot merge it leaves as a
/// conflict. One runs at a time.
/// </summary>
/// <param name="path">Absolute: the folder the person opened, the same scope the listing has.</param>
/// <param name="target">The name the output log gives it.</param>
/// <remarks>Call it from the UI thread: its answers come back on the context that asked.</remarks>
public sealed partial class UpdateViewModel(IWorkingCopyUpdate updates, string path, string target)
    : ObservableObject
{
    /// <summary>
    /// Raised by every check and every update, so an answer to a question asked before either is
    /// recognisably old when it lands.
    /// </summary>
    private int _latestQuestion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    public partial bool IsUpdating { get; private set; }

    /// <summary>What the last update came to; <c>null</c> before the first and once dismissed.</summary>
    [ObservableProperty]
    public partial Notice? Notice { get; private set; }

    /// <summary>
    /// How many changes an update would bring, as <see cref="IncomingCount"/> counts them. Null
    /// before the server has been asked, while an update runs, and when the last check failed —
    /// an old number shown as current would be worse than none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IncomingBadge), nameof(IncomingSentence))]
    public partial int? Incoming { get; private set; }

    public string? IncomingBadge => IncomingText.Badge(Incoming);

    public string? IncomingSentence => IncomingText.Sentence(Incoming);

    /// <summary>Raised once per update, whatever it came to; an output log records these.</summary>
    public event Action<UpdateAttempt>? Attempted;

    /// <summary>
    /// Asks the server what an update would bring. Skipped while an update runs, which asks again
    /// itself once it has finished. A failure clears the count and says nothing: the server being
    /// out of reach is not news until someone presses Update.
    /// </summary>
    public async Task CheckIncomingAsync(CancellationToken cancellationToken)
    {
        if (IsUpdating)
        {
            return;
        }

        var question = ++_latestQuestion;
        int? incoming;
        try
        {
            incoming = await updates.IncomingAsync(path, cancellationToken)
                is IncomingResponse answer
                ? IncomingCount.Of(answer.Incoming.Changes)
                : null;
        }
        catch (DaemonUnreachableException)
        {
            incoming = null;
        }

        if (question == _latestQuestion)
        {
            Incoming = incoming;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync(CancellationToken cancellationToken)
    {
        IsUpdating = true;
        ++_latestQuestion;
        Incoming = null;
        Notice = null;
        UpdateAttempt attempt;
        try
        {
            var response = await updates.UpdateAsync(path, cancellationToken);
            attempt = new UpdateAttempt(target, UpdateNotices.For(response), response);
        }
        catch (DaemonUnreachableException unreachable)
        {
            attempt = new UpdateAttempt(
                target,
                UpdateNotices.Unreachable(unreachable.Message),
                null
            );
        }
        finally
        {
            IsUpdating = false;
        }

        Notice = attempt.Notice;
        Attempted?.Invoke(attempt);

        // Not always zero: a path SVN skipped, or a commit that landed mid-update, is still to come.
        // Not awaited, because the command counts as running until it returns and the button
        // would stay greyed for a whole server round trip.
        _ = CheckIncomingAsync(CancellationToken.None);
    }

    private bool CanUpdate() => !IsUpdating;

    [RelayCommand]
    private void DismissNotice() => Notice = null;
}
