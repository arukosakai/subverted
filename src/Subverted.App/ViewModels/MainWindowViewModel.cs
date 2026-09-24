using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>
/// The window: which working copy is showing, the list of recent ones, and keeping the shown one
/// live while the window is in front.
/// </summary>
/// <param name="open">Builds the view for a path; the one place the daemon is wired in.</param>
/// <param name="comparison">How this platform compares paths, for telling recent copies apart.</param>
/// <param name="history">The History view, shown for whichever working copy is open.</param>
public sealed partial class MainWindowViewModel(
    IRecentWorkingCopyStore store,
    IFolderPicker picker,
    Func<string, WorkingCopyViewModel> open,
    TimeProvider clock,
    StringComparison comparison,
    HistoryViewModel history
) : ObservableObject, IAsyncDisposable
{
    /// <summary>Once a second: the daemon answers warm in about a millisecond.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private StatusPolling? _polling;
    private bool _isInFront;

    public ObservableCollection<RecentWorkingCopy> Recent { get; } = [];

    /// <summary>Every write made from this window, whichever working copy it was in.</summary>
    public OutputLogViewModel Log { get; } = new(clock);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkingCopy))]
    public partial WorkingCopyViewModel? Current { get; private set; }

    public bool HasWorkingCopy => Current is not null;

    private HistoryViewModel? _history;

    /// <summary>Subscribed on first use: a primary constructor has no body to do it in.</summary>
    public HistoryViewModel History => _history ??= WithReturnToChanges(history);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingChanges), nameof(IsShowingHistory))]
    public partial WorkspaceView ShownView { get; private set; } = WorkspaceView.Changes;

    public bool IsShowingChanges => ShownView == WorkspaceView.Changes;

    public bool IsShowingHistory => ShownView == WorkspaceView.History;

    /// <summary>Loads the sidebar and opens the most recent copy, if there is one.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var recent = store.Load();
        Replace(recent, current: null);
        if (recent.Count > 0)
        {
            await ShowAsync(recent[0], cancellationToken);
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync(CancellationToken cancellationToken)
    {
        if (await picker.PickAsync(cancellationToken) is { } path)
        {
            await ShowAsync(path, cancellationToken);
        }
    }

    [RelayCommand]
    private Task SelectAsync(RecentWorkingCopy recent, CancellationToken cancellationToken) =>
        ShowAsync(recent.Path, cancellationToken);

    /// <summary>
    /// Opens a working copy, records it at the top of the recent list and answers it at once — the
    /// first frame should be a listing, not a second's wait for the timer.
    /// </summary>
    public async Task ShowAsync(string path, CancellationToken cancellationToken)
    {
        await StopPollingAsync();

        // From the store rather than the sidebar, so opening a copy before the sidebar has loaded
        // cannot overwrite the kept list with a list of one.
        var recent = RecentWorkingCopies.Opened(store.Load(), path, comparison);
        store.Save(recent);
        Replace(recent, current: path);

        var shown = open(path);
        shown.HistoryRequested += ShowHistoryOfRow;
        shown.Composer.Attempted += Log.Record;
        shown.RevertPrompt.Attempted += Log.Record;
        shown.Updater.Attempted += Log.Record;
        Current = shown;
        ShownView = WorkspaceView.Changes;
        await shown.RefreshAsync(cancellationToken);
        _polling = new StatusPolling(clock, RefreshInterval, shown.RefreshAsync);
        if (_isInFront)
        {
            _polling.Start();
        }
    }

    /// <summary>The window came to the front: answer now, then keep answering.</summary>
    public async Task ActivatedAsync(CancellationToken cancellationToken)
    {
        _isInFront = true;
        if (Current is { } shown && _polling is { IsRunning: false } polling)
        {
            await shown.RefreshAsync(cancellationToken);
            polling.Start();
        }
    }

    /// <summary>The window went to the back: nobody is looking, so nobody is asked.</summary>
    public async Task DeactivatedAsync()
    {
        _isInFront = false;
        if (_polling is { } polling)
        {
            await polling.StopAsync();
        }
    }

    /// <summary>
    /// Shows one path's history — a file's, from its context menu — rather than the whole copy's.
    /// The status poll carries on underneath, so returning to Changes is current at once.
    /// </summary>
    /// <param name="path">Absolute, inside the open working copy.</param>
    public async Task ShowHistoryOfAsync(string path, CancellationToken cancellationToken)
    {
        ShownView = WorkspaceView.History;
        await History.ShowAsync(path, cancellationToken);
    }

    private void ShowHistoryOfRow(string path) =>
        _ = ShowHistoryOfAsync(path, CancellationToken.None);

    /// <summary>
    /// Switches to History for the whole working copy. It is read again only when it was last
    /// showing something else — another copy, or one file — so switching back and forth is free.
    /// </summary>
    [RelayCommand]
    private async Task ShowHistoryAsync(CancellationToken cancellationToken)
    {
        if (Current is not { } shown)
        {
            return;
        }

        ShownView = WorkspaceView.History;
        if (!string.Equals(History.Path, shown.Location, comparison))
        {
            await History.ShowAsync(shown.Location, cancellationToken);
        }
    }

    [RelayCommand]
    private void ShowChanges() => ShownView = WorkspaceView.Changes;

    public async ValueTask DisposeAsync() => await StopPollingAsync();

    private async Task StopPollingAsync()
    {
        if (_polling is { } polling)
        {
            _polling = null;
            await polling.DisposeAsync();
        }
    }

    private HistoryViewModel WithReturnToChanges(HistoryViewModel shown)
    {
        shown.ReturnToChangesRequested += (_, _) => ShowChanges();
        return shown;
    }

    private void Replace(IReadOnlyList<string> recent, string? current)
    {
        Recent.Clear();
        foreach (var path in recent)
        {
            Recent.Add(
                new RecentWorkingCopy(
                    path,
                    FolderName.Of(path),
                    IsCurrent: string.Equals(path, current, comparison)
                )
            );
        }
    }
}
