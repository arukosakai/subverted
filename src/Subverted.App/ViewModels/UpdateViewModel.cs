using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Subverted.App.ViewModels;

/// <summary>
/// The Update button and what the last update came to. Nothing is asked first: an update takes no
/// local change away, and what it cannot merge it leaves as a conflict. One runs at a time.
/// </summary>
/// <param name="path">Absolute: the folder the person opened, the same scope the listing has.</param>
/// <param name="target">The name the output log gives it.</param>
/// <remarks>Call it from the UI thread: its answers come back on the context that asked.</remarks>
public sealed partial class UpdateViewModel(IWorkingCopyUpdate updates, string path, string target)
    : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    public partial bool IsUpdating { get; private set; }

    /// <summary>What the last update came to; <c>null</c> before the first and once dismissed.</summary>
    [ObservableProperty]
    public partial Notice? Notice { get; private set; }

    /// <summary>Raised once per update, whatever it came to; an output log records these.</summary>
    public event Action<UpdateAttempt>? Attempted;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync(CancellationToken cancellationToken)
    {
        IsUpdating = true;
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
    }

    private bool CanUpdate() => !IsUpdating;

    [RelayCommand]
    private void DismissNotice() => Notice = null;
}
