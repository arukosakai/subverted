using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// Takes a line's lock and gives it back, one at a time, and says what came of it. Neither asks
/// first: a lock takes nothing away, and giving one back leaves the file as it is.
/// </summary>
/// <remarks>Call it from the UI thread: its answers come back on the context that asked.</remarks>
public sealed partial class LockViewModel(IWorkingCopyLocks locks) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsWorking { get; private set; }

    /// <summary>What the last lock or unlock came to; <c>null</c> before the first and once dismissed.</summary>
    [ObservableProperty]
    public partial Notice? Notice { get; private set; }

    /// <summary>Raised once per lock sent, whatever it came to; an output log records these.</summary>
    public event Action<LockAttempt>? LockAttempted;

    /// <summary>Raised once per unlock sent, whatever it came to; an output log records these.</summary>
    public event Action<UnlockAttempt>? UnlockAttempted;

    /// <param name="target">The file, relative to <paramref name="root"/>.</param>
    /// <remarks>Ignored while a lock or an unlock is running.</remarks>
    public async Task LockAsync(string target, string root, CancellationToken cancellationToken)
    {
        if (await SendAsync(
                () => locks.LockAsync(DiffTarget.PathOf(root, target), cancellationToken),
                response => LockNotices.Locked(target, response)
            )
            is { } sent)
        {
            LockAttempted?.Invoke(new LockAttempt(target, sent.Notice, sent.Answer));
        }
    }

    /// <param name="target">The file, relative to <paramref name="root"/>.</param>
    /// <remarks>Ignored while a lock or an unlock is running.</remarks>
    public async Task UnlockAsync(string target, string root, CancellationToken cancellationToken)
    {
        if (await SendAsync(
                () => locks.UnlockAsync(DiffTarget.PathOf(root, target), cancellationToken),
                response => LockNotices.Unlocked(target, response)
            )
            is { } sent)
        {
            UnlockAttempted?.Invoke(new UnlockAttempt(target, sent.Notice, sent.Answer));
        }
    }

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    /// <returns>What was said and what the daemon answered; <c>null</c> when nothing was sent.</returns>
    private async Task<(Notice Notice, DaemonResponse? Answer)?> SendAsync(
        Func<Task<DaemonResponse>> send,
        Func<DaemonResponse, Notice> describe
    )
    {
        if (IsWorking)
        {
            return null;
        }

        IsWorking = true;
        Notice = null;
        (Notice Notice, DaemonResponse? Answer) outcome;
        try
        {
            var response = await send();
            outcome = (describe(response), response);
        }
        catch (DaemonUnreachableException unreachable)
        {
            outcome = (LockNotices.Unreachable(unreachable.Message), null);
        }
        finally
        {
            IsWorking = false;
        }

        Notice = outcome.Notice;
        return outcome;
    }
}
