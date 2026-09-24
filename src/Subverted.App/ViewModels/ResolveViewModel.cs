using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;
using Subverted.Frontend;

namespace Subverted.App.ViewModels;

/// <summary>
/// Settles a line's conflicts with the version the person picked. One that throws their own
/// version away asks first with the exact list; one that keeps it is sent at once.
/// </summary>
/// <remarks>Call it from the UI thread: its answers come back on the context that asked.</remarks>
public sealed partial class ResolveViewModel(IWorkingCopyResolve resolves) : ObservableObject
{
    private string _root = "";

    /// <summary>The question on screen; <c>null</c> when none is being asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial ResolveScope? Pending { get; private set; }

    public bool IsAsking => Pending is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(CancelCommand))]
    public partial bool IsResolving { get; private set; }

    /// <summary>What the last resolve came to; <c>null</c> before the first and once dismissed.</summary>
    [ObservableProperty]
    public partial Notice? Notice { get; private set; }

    /// <summary>Raised once per resolve sent, whatever it came to; an output log records these.</summary>
    public event Action<ResolveAttempt>? Attempted;

    /// <summary>Asks first or sends at once; ignored while a resolve is running.</summary>
    /// <param name="root">The working-copy root the scope's paths are relative to.</param>
    public Task ResolveAsync(ResolveScope scope, string root, CancellationToken cancellationToken)
    {
        if (IsResolving)
        {
            return Task.CompletedTask;
        }

        _root = root;
        Notice = null;
        if (ResolutionRisk.OverwritesLocalWork(scope.Resolution))
        {
            Pending = scope;
            return Task.CompletedTask;
        }

        return SendAsync(scope, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task ConfirmAsync(CancellationToken cancellationToken) =>
        SendAsync(Pending!, cancellationToken);

    private bool CanConfirm() => Pending is not null && !IsResolving;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => Pending = null;

    private bool CanCancel() => !IsResolving;

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    private async Task SendAsync(ResolveScope scope, CancellationToken cancellationToken)
    {
        IsResolving = true;
        ResolveAttempt attempt;
        try
        {
            var response = await resolves.ResolveAsync(
                DiffTarget.PathOf(_root, scope.Target),
                scope.Resolution,
                cancellationToken
            );
            attempt = new ResolveAttempt(
                scope.Target,
                scope.Resolution,
                ResolveNotices.For(scope.Target, scope.Resolution, response),
                response
            );
        }
        catch (DaemonUnreachableException unreachable)
        {
            attempt = new ResolveAttempt(
                scope.Target,
                scope.Resolution,
                ResolveNotices.Unreachable(unreachable.Message),
                null
            );
        }
        finally
        {
            IsResolving = false;
            Pending = null;
        }

        Notice = attempt.Notice;
        Attempted?.Invoke(attempt);
    }
}
