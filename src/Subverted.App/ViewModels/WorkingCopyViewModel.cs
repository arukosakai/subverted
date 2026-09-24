using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;
using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>One working copy as the window shows it: what changed, and whether that answer is real.</summary>
/// <param name="diff">Shows whichever row is selected; this view model tells it when that changes.</param>
/// <param name="launcher">Opens a row's file, for Enter.</param>
/// <param name="revealer">Shows a line's path in the file manager, for the context menu.</param>
/// <param name="clipboard">Takes a line's path, for the context menu.</param>
/// <param name="commits">Sends the ticked lines, for the commit box.</param>
/// <param name="reverts">Reverts a confirmed line, for the context menu.</param>
/// <param name="resolves">Settles a line's conflicts, for the context menu.</param>
/// <param name="locks">Takes and gives back a file's lock, for the context menu.</param>
/// <param name="updates">Brings the opened folder up to date, for the Update button.</param>
/// <param name="reviews">Shows the commit review window, for the commit box's Review button.</param>
/// <param name="reviewDiff">A fresh diff pane for each review, apart from <paramref name="diff"/>.</param>
public sealed partial class WorkingCopyViewModel(
    string path,
    IWorkingCopyStatus status,
    DiffPaneViewModel diff,
    IFileLauncher launcher,
    IFileRevealer revealer,
    ITextClipboard clipboard,
    IWorkingCopyCommit commits,
    IWorkingCopyRevert reverts,
    IWorkingCopyResolve resolves,
    IWorkingCopyLocks locks,
    IWorkingCopyUpdate updates,
    ICommitReviewOpener reviews,
    Func<DiffPaneViewModel> reviewDiff
) : ObservableObject, ICommitTicks
{
    private readonly TickedPaths _ticks = new();
    private readonly CollapsedFolders _collapsed = new();
    private IReadOnlyList<FolderLine> _tree = [];
    private IReadOnlySet<string> _shown = new HashSet<string>();

    /// <summary>Every row of the last listing, unmodified ones included when it was asked for all.</summary>
    private IReadOnlyList<ChangeRow> _rows = [];

    /// <summary>What the table and the tree are drawn from: <see cref="_rows"/> as <see cref="Listing"/> keeps them.</summary>
    private IReadOnlyList<ChangeRow> _lines = [];

    /// <summary>The scan the listing on screen came from, so an unchanged working copy is not sent again.</summary>
    private Guid? _heldScan;
    private CommitComposerViewModel? _composer;
    private RevertPromptViewModel? _revertPrompt;
    private ResolveViewModel? _resolver;
    private LockViewModel? _locker;
    private UpdateViewModel? _updater;

    /// <summary>
    /// The record the diff was last asked for. A resync updates a changed line's row in place, and
    /// comparing against this one is how it knows the diff on screen went out of date.
    /// </summary>
    private ChangeRow? _followed;

    private bool _isRelayingOut;
    private bool _isRelayingFolders;
    private Action<string>? _historyRequested;

    /// <summary>The path the person opened, which may be anywhere inside the working copy.</summary>
    public string Path { get; } = path;

    /// <summary>
    /// Every change the daemon listed, pinned rows first (see <see cref="ChangeOrder"/>) — what the
    /// header counts and a commit chooses from, whatever the filter is hiding and whatever
    /// <see cref="Listing"/> says: an unmodified file is never one of them.
    /// </summary>
    public ObservableCollection<ChangeRow> Changes { get; } = [];

    /// <summary>
    /// What the list shows: the changes — and in <see cref="ListedNodes.All"/> every unmodified file
    /// too — through the filter, flat or as a tree.
    /// </summary>
    public ObservableCollection<ChangeListEntry> Entries { get; } = [];

    /// <summary>The directory pane: every folder holding a listed file, the root first, less what is collapsed.</summary>
    public ObservableCollection<FolderEntry> Folders { get; } = [];

    /// <summary>
    /// Changes only, or every versioned file as well. Changes is the default; All is where a file
    /// nobody has touched yet can be locked from.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListingAll))]
    public partial ListedNodes Listing { get; private set; } = ListedNodes.Changes;

    public bool IsListingAll => Listing == ListedNodes.All;

    /// <summary>
    /// The folder the table is narrowed to; the root, or nothing chosen, shows every change. It
    /// narrows exactly as the filter does, so what it hides is neither counted nor sent.
    /// </summary>
    [ObservableProperty]
    public partial FolderEntry? SelectedFolder { get; set; }

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
    /// The ticked paths, relative to the root. A path starts at its <see cref="DefaultTick"/>, its
    /// tick outlives every resync that still lists it, and is dropped by the first one that does not.
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

    /// <summary>The message and the button that commit what is ticked and shown.</summary>
    public CommitComposerViewModel Composer => _composer ??= new(commits, Untick, ReviewAsync);

    public RevertPromptViewModel RevertPrompt => _revertPrompt ??= new(reverts);

    public ResolveViewModel Resolver => _resolver ??= new(resolves);

    public LockViewModel Locker => _locker ??= new(locks);

    /// <summary>Updates <see cref="Path"/>, the folder the listing is scoped to, not the whole root.</summary>
    public UpdateViewModel Updater => _updater ??= new(updates, Path, FolderName.Of(Path));

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
    /// No change is listed, and that is an answer rather than the absence of one — never true
    /// before the daemon has replied. A locked but unchanged file is no change, so it leaves the copy
    /// clean, though it is still listed.
    /// </summary>
    public bool IsClean => State == WorkingCopyState.Ready && Changes.Count == 0;

    /// <summary>
    /// Clean, with nothing else to list either: the view says so and nothing more. Listing all,
    /// a clean copy shows its files instead, since that is where one is locked from.
    /// </summary>
    public bool ShowsCleanMessage => IsClean && _lines.Count == 0;

    /// <summary>
    /// There is a change to commit — a stale listing still counts. Until there is, the composer has
    /// nothing to say.
    /// </summary>
    public bool HasChanges => Changes.Count > 0;

    /// <summary>
    /// There is a line to act on — a change, or an unmodified file listing all. Until there is, the
    /// tree and the diff have nothing to say, and the view shows only why.
    /// </summary>
    public bool HasLines => _lines.Count > 0;

    /// <summary>
    /// Something is wrong and there is no earlier listing to fall back on, so the view says what
    /// is wrong instead.
    /// </summary>
    public bool IsBlocked => Headline is not null && _lines.Count == 0;

    /// <summary>
    /// Something is wrong but an earlier listing is on screen. It stays, marked as possibly out of
    /// date, rather than making someone's changes vanish for the second a daemon takes to restart.
    /// </summary>
    public bool IsStale => Headline is not null && _lines.Count > 0;

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
        var asked = Listing;
        DaemonResponse response;
        try
        {
            response = await status.ReadAsync(Path, asked, _heldScan, cancellationToken);
        }
        catch (DaemonUnreachableException unreachable)
        {
            Show(WorkingCopyState.Unreachable, unreachable.Message);
            return;
        }

        // The toggle moved while this was asked; the refresh it started answers for what it shows.
        if (asked != Listing)
        {
            return;
        }

        switch (response)
        {
            case StatusResponse listing:
                ShowListing(listing);
                break;

            // What is on screen is what would have been sent: nothing to redo, nothing to raise.
            case StatusUnchangedResponse:
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
        _rows = ChangeOrder.Of(ChangeRows.From(listing.Entries, listing.UnrecordedMoves));
        _heldScan = listing.ScanId;
        var changes = _rows.Where(row => !row.IsUnmodified).ToList();

        RootPath = listing.Info.RootPath;
        ChangeListSynchronizer.Apply(Changes, changes);

        // Only changes are followed, so a file first seen unmodified still takes its default tick
        // the moment it changes — the same commit whichever way the table is listed.
        _ticks.Follow(changes);
        OnPropertyChanged(nameof(Ticked));
        ShowLines();
        RepositoryRoot = listing.Info.RepositoryRoot;
        Name = FolderName.Of(listing.Info.RootPath);
        Summary = ChangeSummary.Of(changes);
        Message = null;
        State = WorkingCopyState.Ready;
        RaiseDerived();
    }

    /// <summary>Draws the tree and the table from the last listing, as <see cref="Listing"/> keeps it.</summary>
    private void ShowLines()
    {
        _lines = ListedLines.Of(_rows, Listing);
        ShowFolders(_lines, FolderName.Of(Location));
        LayOut();
    }

    [RelayCommand]
    private Task ListAllAsync(CancellationToken cancellationToken) =>
        ListAsync(ListedNodes.All, cancellationToken);

    [RelayCommand]
    private Task ListChangesAsync(CancellationToken cancellationToken) =>
        ListAsync(ListedNodes.Changes, cancellationToken);

    /// <summary>
    /// Redraws from what is already held at once — going back to changes needs nothing new — then
    /// asks for the listing afresh, since a scan held for the other listing says nothing about this one.
    /// </summary>
    private async Task ListAsync(ListedNodes listed, CancellationToken cancellationToken)
    {
        if (Listing == listed)
        {
            return;
        }

        Listing = listed;
        _heldScan = null;
        ShowLines();
        RaiseDerived();
        await RefreshAsync(cancellationToken);
    }

    partial void OnFilterChanged(string value) => LayOut();

    partial void OnSelectedFolderChanged(FolderEntry? value)
    {
        if (!_isRelayingFolders)
        {
            LayOut();
        }
    }

    /// <summary>
    /// Brings the pane up to date and keeps the chosen folder chosen; a folder that no longer holds
    /// anything falls back to the root rather than narrowing the table to nothing.
    /// </summary>
    private void ShowFolders(IReadOnlyList<ChangeRow> rows, string rootName)
    {
        _tree = ChangeFolders.Of(rows, rootName);
        _collapsed.Follow(_tree);
        ShowOutline();
    }

    /// <summary>
    /// Lays the pane out for what is collapsed. A chosen folder that went out of sight hands the
    /// choice to the collapsed folder above it, so the table is never narrowed by a hidden line.
    /// </summary>
    /// <returns>Whether the chosen folder moved, and with it what the table should show.</returns>
    private bool ShowOutline()
    {
        var chosen = SelectedFolder?.Content.RelPath ?? "";
        var landing = FolderOutline.Landing(chosen, _collapsed.Paths);
        _isRelayingFolders = true;
        try
        {
            ListSlotSynchronizer.Apply(
                Folders,
                FolderOutline.Shown(_tree, _collapsed.Paths),
                line => line.RelPath,
                line => new FolderEntry(line)
            );
            SelectedFolder =
                Folders.FirstOrDefault(folder => folder.Content.RelPath == landing)
                ?? Folders.FirstOrDefault();
        }
        finally
        {
            _isRelayingFolders = false;
        }

        return (SelectedFolder?.Content.RelPath ?? "") != chosen;
    }

    /// <summary>The chevron: collapses an open folder, opens a collapsed one, leaves the choice where it is unless it went out of sight.</summary>
    [RelayCommand]
    private void ToggleFolder(FolderEntry? folder)
    {
        if (folder?.Content is not { HasSubfolders: true } line)
        {
            return;
        }

        if (line.IsCollapsed)
        {
            _collapsed.Expand(line.RelPath);
        }
        else
        {
            _collapsed.Collapse(line.RelPath);
        }

        Relayout();
    }

    /// <summary>Right in a tree: a collapsed folder opens; anything else stays as it is.</summary>
    [RelayCommand]
    private void StepIn()
    {
        if (SelectedFolder?.Content is { CanExpand: true } line)
        {
            _collapsed.Expand(line.RelPath);
            Relayout();
        }
    }

    /// <summary>Left in a tree: an open folder collapses; a collapsed one, or one with nothing under it, hands the choice to its parent.</summary>
    [RelayCommand]
    private void StepOut()
    {
        switch (SelectedFolder?.Content)
        {
            case { CanCollapse: true } line:
                _collapsed.Collapse(line.RelPath);
                Relayout();
                break;

            case { Parent: { } parent }:
                SelectedFolder = Folders.First(folder => folder.Content.RelPath == parent);
                break;
        }
    }

    private void Relayout()
    {
        if (ShowOutline())
        {
            LayOut();
        }
    }

    partial void OnIsTreeChanged(bool value) => LayOut();

    partial void OnSelectedEntryChanged(ChangeListEntry? value)
    {
        OnPropertyChanged(nameof(IsResolveOffered));
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
        var folder = SelectedFolder?.Content.RelPath ?? "";
        var kept = ChangeFilter.Apply(
            _lines.Where(row => ChangeFolders.Contains(folder, row)),
            Filter
        );
        var items = IsTree ? ChangeTree.Of(kept) : FlatChangeList.Of(kept);

        // The list control writes a selection it dropped back through the binding, mid-merge.
        _isRelayingOut = true;
        try
        {
            ListSlotSynchronizer.Apply(Entries, items, item => item.Key, Create);
            _shown = kept.Select(row => row.RelPath).ToHashSet(StringComparer.Ordinal);
            ShowTicks();
            SelectedEntry = Entries.FirstOrDefault(entry => entry.Key == selectedKey);
        }
        finally
        {
            _isRelayingOut = false;
        }

        HiddenText = HiddenChanges.Text(Changes.Count, kept.Count(row => !row.IsUnmodified));
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
    private void ToggleTick(ChangeListEntry? entry) => Toggle(entry!.Row!.RelPath);

    bool ICommitTicks.IsTicked(string relPath) => _ticks.IsTicked(relPath);

    void ICommitTicks.Toggle(string relPath) => Toggle(relPath);

    private void Toggle(string relPath)
    {
        _ticks.Toggle(relPath);
        OnPropertyChanged(nameof(Ticked));
        ShowTicks();
        ToggleTickCommand.NotifyCanExecuteChanged();
    }

    private static bool CanTick(ChangeListEntry? entry) => entry?.IsTickable == true;

    private async Task ReviewAsync()
    {
        using var review = new CommitReviewViewModel(Composer, reviewDiff(), this, Location);
        await reviews.OpenAsync(review);
    }

    /// <summary>After a commit reached a revision: what it sent is no longer ticked.</summary>
    private void Untick(IReadOnlyList<string> relPaths)
    {
        _ticks.Untick(relPaths);
        OnPropertyChanged(nameof(Ticked));
        ShowTicks();
        ToggleTickCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Puts every line's tick back from the paths, marks the lines a directory decides, and tells
    /// the commit box what it would now send. A tick can change what the lines below it may do.
    /// </summary>
    private void ShowTicks()
    {
        var selection = TickedSelection.Of(Changes, _ticks.Paths, _shown);
        foreach (var entry in Entries)
        {
            entry.IsTicked = entry.Row is { } row && _ticks.IsTicked(row.RelPath);
            entry.IsDecidedByFolder =
                entry.Row is { } decided && selection.DecidedByFolder.Contains(decided.RelPath);
        }

        Composer.Offer(selection, Location);
    }

    [RelayCommand(CanExecute = nameof(CanRevert))]
    private void Revert(ChangeListEntry? entry)
    {
        var target = entry!.Row!;
        RevertPrompt.Ask(RevertConfirmation.For(target, Changes), Location, () => Relist(target));
    }

    /// <summary>The same revert against today's listing; nothing to do once the target is gone from it.</summary>
    private RevertConfirmation Relist(ChangeRow asked) =>
        Changes.FirstOrDefault(row => row.RelPath == asked.RelPath) is { } current
            ? RevertConfirmation.For(current, Changes)
            : new RevertConfirmation(asked.RelPath, []);

    /// <summary>Only where revert would do something: never a rename, an unversioned file or a folder that only holds others.</summary>
    private bool CanRevert(ChangeListEntry? entry) =>
        entry?.Row is { RenamedFrom: null } row
        && RevertConfirmation.For(row, Changes).Lines.Count > 0;

    [RelayCommand(CanExecute = nameof(CanResolve))]
    private Task KeepMineAsync(ChangeListEntry? entry, CancellationToken cancellationToken) =>
        ResolveAsync(entry!, ConflictResolution.Mine, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanResolve))]
    private Task TakeTheirsAsync(ChangeListEntry? entry, CancellationToken cancellationToken) =>
        ResolveAsync(entry!, ConflictResolution.Theirs, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanResolve))]
    private Task MarkResolvedAsync(ChangeListEntry? entry, CancellationToken cancellationToken) =>
        ResolveAsync(entry!, ConflictResolution.Working, cancellationToken);

    private Task ResolveAsync(
        ChangeListEntry entry,
        ConflictResolution resolution,
        CancellationToken cancellationToken
    )
    {
        var target = entry.Row!;
        return Resolver.ResolveAsync(
            ResolveScope.For(target, resolution, Changes),
            Location,
            () => Rescope(target, resolution),
            cancellationToken
        );
    }

    private ResolveScope Rescope(ChangeRow asked, ConflictResolution resolution) =>
        Changes.FirstOrDefault(row => row.RelPath == asked.RelPath) is { } current
            ? ResolveScope.For(current, resolution, Changes)
            : new ResolveScope(asked.RelPath, resolution, []);

    /// <summary>
    /// Whether the picked line has anything to resolve, for the submenu that holds the three: a
    /// header has no command of its own, so it would otherwise stay enabled over disabled items.
    /// </summary>
    public bool IsResolveOffered => CanResolve(SelectedEntry);

    /// <summary>Only where there is a conflict to settle: the line's own, or one beneath its folder.</summary>
    private bool CanResolve(ChangeListEntry? entry) =>
        entry?.Row is { } row
        && ResolveScope.For(row, ConflictResolution.Working, Changes).Lines.Count > 0;

    [RelayCommand(CanExecute = nameof(CanLock))]
    private Task LockAsync(ChangeListEntry? entry, CancellationToken cancellationToken) =>
        Locker.LockAsync(entry!.Row!.RelPath, Location, cancellationToken);

    /// <summary>Only a file the repository has at that path, and not one whose lock is already held here.</summary>
    private static bool CanLock(ChangeListEntry? entry) =>
        entry?.Row is { } row && LockOffer.CanLock(row);

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private Task UnlockAsync(ChangeListEntry? entry, CancellationToken cancellationToken) =>
        Locker.UnlockAsync(entry!.Row!.RelPath, Location, cancellationToken);

    private static bool CanUnlock(ChangeListEntry? entry) =>
        entry?.Row is { } row && LockOffer.CanUnlock(row);

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
    private void ClearFilter()
    {
        Filter = "";
        SelectedFolder = Folders.FirstOrDefault();
    }

    private string PathOf(string relPath) => DiffTarget.PathOf(Location, relPath);

    private static ChangeListEntry Create(ChangeListItem item) => new(item);

    /// <summary>A line's content can change under the same entry, and with it what may be done to it.</summary>
    private void RaiseLineCommands()
    {
        ToggleTickCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        ShowHistoryCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
        KeepMineCommand.NotifyCanExecuteChanged();
        TakeTheirsCommand.NotifyCanExecuteChanged();
        MarkResolvedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsResolveOffered));
        LockCommand.NotifyCanExecuteChanged();
        UnlockCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// A failure leaves <see cref="Changes"/> as it was; see <see cref="IsStale"/>. The next answer
    /// is a whole listing, since only a listing puts <see cref="State"/> back.
    /// </summary>
    private void Show(WorkingCopyState state, string message)
    {
        _heldScan = null;
        Message = message;
        State = state;
        RaiseDerived();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(ShowsCleanMessage));
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasLines));
    }
}
