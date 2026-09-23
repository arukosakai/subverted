using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>One working copy as the window shows it: what changed, and whether that answer is real.</summary>
/// <param name="diff">Shows whichever row is selected; this view model tells it when that changes.</param>
/// <param name="launcher">Opens a row's file, for Enter.</param>
/// <param name="revealer">Shows a line's path in the file manager, for the context menu.</param>
/// <param name="clipboard">Takes a line's path, for the context menu.</param>
public sealed partial class WorkingCopyViewModel(
    string path,
    IWorkingCopyStatus status,
    DiffPaneViewModel diff,
    IFileLauncher launcher,
    IFileRevealer revealer,
    ITextClipboard clipboard
) : ObservableObject
{
    private readonly TickedPaths _ticks = new();

    /// <summary>
    /// The record the diff was last asked for. A resync updates a changed line's row in place, and
    /// comparing against this one is how it knows the diff on screen went out of date.
    /// </summary>
    private ChangeRow? _followed;

    private bool _isRelayingOut;
    private Action<string>? _historyRequested;

    /// <summary>The path the person opened, which may be anywhere inside the working copy.</summary>
    public string Path { get; } = path;

    /// <summary>
    /// Everything the daemon listed, pinned rows first (see <see cref="ChangeOrder"/>) — what the
    /// header counts, whatever the filter is hiding.
    /// </summary>
    public ObservableCollection<ChangeRow> Changes { get; } = [];

    /// <summary>What the list shows: <see cref="Changes"/> through the filter, flat or as a tree.</summary>
    public ObservableCollection<ChangeListEntry> Entries { get; } = [];

    /// <summary>
    /// The line the person picked. It survives the once-a-second resync, a change of layout and a
    /// filter that still shows it; a filter that hides it takes the selection away.
    /// </summary>
    [ObservableProperty]
    public partial ChangeListEntry? SelectedEntry { get; set; }

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlat))]
    public partial bool IsTree { get; private set; }

    public bool IsFlat => !IsTree;

    /// <summary>"3 changes hidden" while the filter keeps some out of sight; <c>null</c> otherwise.</summary>
    [ObservableProperty]
    public partial string? HiddenText { get; private set; }

    /// <summary>
    /// The ticked paths, relative to the root. A tick outlives every resync that still lists its
    /// path, and is dropped by the first one that does not.
    /// </summary>
    public IReadOnlySet<string> Ticked => _ticks.Paths;

    /// <summary>
    /// The person asked for a change's history, with its absolute path. Until something handles
    /// it, the context menu's item for it is disabled.
    /// </summary>
    public event Action<string>? HistoryRequested
    {
        add
        {
            _historyRequested += value;
            ShowHistoryCommand.NotifyCanExecuteChanged();
        }
        remove
        {
            _historyRequested -= value;
            ShowHistoryCommand.NotifyCanExecuteChanged();
        }
    }

    public DiffPaneViewModel Diff { get; } = diff;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Headline))]
    public partial WorkingCopyState State { get; private set; } = WorkingCopyState.Loading;

    [ObservableProperty]
    public partial string Name { get; private set; } = FolderName.Of(path);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Location))]
    public partial string? RootPath { get; private set; }

    /// <summary>Where this is on disk: the root once the daemon has said, the opened path until then.</summary>
    public string Location => RootPath ?? Path;

    [ObservableProperty]
    public partial string? RepositoryRoot { get; private set; }

    /// <summary>Why the view is not showing a listing, when it is not.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ChangeCount> Summary { get; private set; } = [];

    /// <summary>
    /// Nothing is listed, and that is an answer rather than the absence of one — never true before
    /// the daemon has replied. A locked but unchanged file is listed, so it is not clean here.
    /// </summary>
    public bool IsClean => State == WorkingCopyState.Ready && Changes.Count == 0;

    /// <summary>
    /// Something is wrong and there is no earlier listing to fall back on, so the view says what
    /// is wrong instead.
    /// </summary>
    public bool IsBlocked => Headline is not null && Changes.Count == 0;

    /// <summary>
    /// Something is wrong but an earlier listing is on screen. It stays, marked as possibly out of
    /// date, rather than making someone's changes vanish for the second a daemon takes to restart.
    /// </summary>
    public bool IsStale => Headline is not null && Changes.Count > 0;

    /// <summary>What the view says in large type when it cannot show a listing.</summary>
    public string? Headline =>
        State switch
        {
            WorkingCopyState.NotAWorkingCopy => "Not a working copy",
            WorkingCopyState.Unreachable => "The daemon is not answering",
            WorkingCopyState.Failed => "Status could not be read",
            _ => null,
        };

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        DaemonResponse response;
        try
        {
            response = await status.ReadAsync(Path, cancellationToken);
        }
        catch (DaemonUnreachableException unreachable)
        {
            Show(WorkingCopyState.Unreachable, unreachable.Message);
            return;
        }

        switch (response)
        {
            case StatusResponse listing:
                ShowListing(listing);
                break;

            case ErrorResponse { Kind: DaemonErrorKind.NotAWorkingCopy } error:
                Show(WorkingCopyState.NotAWorkingCopy, error.Message);
                break;

            case ErrorResponse error:
                Show(WorkingCopyState.Failed, error.Message);
                break;

            default:
                Show(
                    WorkingCopyState.Failed,
                    $"The daemon answered with {response.GetType().Name}, which is not a listing."
                );
                break;
        }
    }

    private void ShowListing(StatusResponse listing)
    {
        var rows = ChangeOrder.Of(listing.Entries.Select(ChangeRow.From));

        RootPath = listing.Info.RootPath;
        ChangeListSynchronizer.Apply(Changes, rows);
        _ticks.KeepOnly(rows.Select(row => row.RelPath));
        OnPropertyChanged(nameof(Ticked));
        LayOut();
        RepositoryRoot = listing.Info.RepositoryRoot;
        Name = FolderName.Of(listing.Info.RootPath);
        Summary = ChangeSummary.Of(rows);
        Message = null;
        State = WorkingCopyState.Ready;
        RaiseDerived();
    }

    partial void OnFilterChanged(string value) => LayOut();

    partial void OnIsTreeChanged(bool value) => LayOut();

    partial void OnSelectedEntryChanged(ChangeListEntry? value)
    {
        if (_isRelayingOut)
        {
            return;
        }

        _followed = value?.Row;
        if (_followed is null)
        {
            Diff.Clear();
            return;
        }

        _ = Diff.SelectAsync(_followed, PathOf(_followed.RelPath));
    }

    /// <summary>
    /// Brings the shown lines up to date with the listing, the filter and the layout, then puts
    /// the selection back on its line and asks for the diff again if that line's change changed.
    /// </summary>
    private void LayOut()
    {
        var selectedKey = SelectedEntry?.Key;
        var shownFor = _followed;
        var kept = ChangeFilter.Apply(Changes, Filter);
        var items = IsTree ? ChangeTree.Of(kept) : FlatChangeList.Of(kept);

        // The list control writes a selection it dropped back through the binding, mid-merge.
        _isRelayingOut = true;
        try
        {
            ListSlotSynchronizer.Apply(Entries, items, item => item.Key, Create);
            foreach (var entry in Entries)
            {
                entry.IsTicked = entry.Row is { } row && _ticks.IsTicked(row.RelPath);
            }

            SelectedEntry = Entries.FirstOrDefault(entry => entry.Key == selectedKey);
        }
        finally
        {
            _isRelayingOut = false;
        }

        HiddenText = HiddenChanges.Text(Changes.Count, kept.Count);
        RaiseLineCommands();
        Refollow(shownFor, SelectedEntry?.Row);
    }

    private void Refollow(ChangeRow? shownFor, ChangeRow? listed)
    {
        _followed = listed;
        if (shownFor is null)
        {
            return;
        }

        if (listed is null)
        {
            Diff.Clear();
            return;
        }

        if (DiffFreshness.NeedsRefetch(shownFor, listed))
        {
            _ = Diff.RefetchAsync(listed, PathOf(listed.RelPath));
        }
    }

    [RelayCommand(CanExecute = nameof(CanTick))]
    private void ToggleTick(ChangeListEntry? entry)
    {
        entry!.IsTicked = _ticks.Toggle(entry.Row!.RelPath);
        OnPropertyChanged(nameof(Ticked));
    }

    private static bool CanTick(ChangeListEntry? entry) => entry?.Row is not null;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenAsync(ChangeListEntry? entry) =>
        launcher.OpenAsync(PathOf(entry!.Content.RelPath));

    /// <summary>A folder that only holds changes is not one to open; Enter on it does nothing.</summary>
    private static bool CanOpen(ChangeListEntry? entry) => entry?.Row is not null;

    [RelayCommand(CanExecute = nameof(IsLine))]
    private Task RevealAsync(ChangeListEntry? entry) =>
        revealer.RevealAsync(PathOf(entry!.Content.RelPath));

    [RelayCommand(CanExecute = nameof(IsLine))]
    private Task CopyPathAsync(ChangeListEntry? entry) =>
        clipboard.CopyAsync(PathOf(entry!.Content.RelPath));

    private static bool IsLine(ChangeListEntry? entry) => entry is not null;

    [RelayCommand(CanExecute = nameof(CanShowHistory))]
    private void ShowHistory(ChangeListEntry? entry) =>
        _historyRequested!.Invoke(PathOf(entry!.Content.RelPath));

    private bool CanShowHistory(ChangeListEntry? entry) =>
        _historyRequested is not null && entry?.Row is { HasHistory: true };

    [RelayCommand]
    private void ShowFlat() => IsTree = false;

    [RelayCommand]
    private void ShowTree() => IsTree = true;

    [RelayCommand]
    private void ClearFilter() => Filter = "";

    private string PathOf(string relPath) => DiffTarget.PathOf(Location, relPath);

    private static ChangeListEntry Create(ChangeListItem item) => new(item);

    /// <summary>A line's content can change under the same entry, and with it what may be done to it.</summary>
    private void RaiseLineCommands()
    {
        ToggleTickCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        ShowHistoryCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A failure leaves <see cref="Changes"/> as it was; see <see cref="IsStale"/>.</summary>
    private void Show(WorkingCopyState state, string message)
    {
        Message = message;
        State = state;
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(IsStale));
    }
}
