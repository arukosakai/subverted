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
    private Func<ResolveScope>? _relist;

    /// <summary>The question on screen; <c>null</c> when none is being asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial ResolveScope? Pending { get; private set; }

    public bool IsAsking => Pending is not null;

    /// <summary>The list on screen is not the one first shown: the listing changed under the question.</summary>
    [ObservableProperty]
    public partial bool HasChangedSinceAsked { get; private set; }

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
    /// <param name="relist">
    /// The same scope against the listing as it stands when confirmed; a question whose list has
    /// changed is shown again rather than sent.
    /// </param>
    public Task ResolveAsync(
        ResolveScope scope,
        string root,
        Func<ResolveScope> relist,
        CancellationToken cancellationToken
    )
    {
        if (IsResolving)
        {
            return Task.CompletedTask;
        }

        _root = root;
        Notice = null;
        if (ResolutionRisk.OverwritesLocalWork(scope.Resolution))
        {
            _relist = relist;
            HasChangedSinceAsked = false;
            Pending = scope;
            return Task.CompletedTask;
        }

        return SendAsync(scope, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task ConfirmAsync(CancellationToken cancellationToken)
    {
        var current = _relist!();
        if (current.Lines.Count == 0)
        {
            Notice = ResolveNotices.NothingLeft(Pending!.Target);
            Cancel();
            return Task.CompletedTask;
        }

        if (!current.Lines.SequenceEqual(Pending!.Lines))
        {
            HasChangedSinceAsked = true;
            Pending = current;
            return Task.CompletedTask;
        }

        return SendAsync(current, cancellationToken);
    }

    private bool CanConfirm() => Pending is not null && !IsResolving;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        HasChangedSinceAsked = false;
        Pending = null;
    }

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
            HasChangedSinceAsked = false;
            Pending = null;
        }

        Notice = attempt.Notice;
        Attempted?.Invoke(attempt);
    }
}
