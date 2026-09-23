using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;
using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// The History view: a path's revisions newest first, read a page at a time from HEAD, with the
/// selected revision's changed paths and the selected path's diff beneath. Showing another path
/// drops everything about the last one, including answers still on their way.
/// </summary>
/// <remarks>Call it from the UI thread: its answers come back on the context that asked.</remarks>
/// <param name="clock">Only its zone is read, for showing commit times on the viewer's clock.</param>
public sealed partial class HistoryViewModel(
    IRevisionHistory history,
    RevisionDiffPaneViewModel diff,
    TimeProvider clock
) : ObservableObject
{
    /// <summary>Enough rows to fill a tall window, so the list can scroll and ask for more.</summary>
    public const int PageSize = 50;

    private readonly List<RevisionRow> _loaded = [];
    private HistoryCursor _cursor = HistoryCursor.Fresh;

    /// <summary>The path being shown and what cancels its answers; null before the first.</summary>
    private Showing? _showing;
    private bool _isReadingPage;

    /// <summary>The list control writes a replaced row's dropped selection back mid-sync.</summary>
    private bool _isSyncing;

    /// <summary>Raised by the pinned "Local changes" row; the window switches back to Changes.</summary>
    public event EventHandler? ReturnToChangesRequested;

    /// <summary>The path whose history is shown, or null before one was asked for.</summary>
    [ObservableProperty]
    public partial string? Path { get; private set; }

    /// <summary>What is shown: the loaded revisions that match the search, newest first.</summary>
    public ObservableCollection<RevisionListItem> Items { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial RevisionListItem? SelectedItem { get; set; }

    /// <summary>The picked revision, whose paths and message the lower half shows.</summary>
    [ObservableProperty]
    public partial RevisionRow? SelectedRevision { get; private set; }

    [ObservableProperty]
    public partial ChangedPathRow? SelectedPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial HistoryState State { get; private set; } = HistoryState.NothingShown;

    /// <summary>Why nothing is listed, or — while <see cref="HistoryState.Ready"/> — why no more could be read.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>A page below the first is on its way.</summary>
    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    /// <summary>This working copy's BASE range, or null until the daemon has said.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BaseText))]
    public partial BaseRevisionRange? BaseRange { get; private set; }

    public string? BaseText => BaseRange is { } range ? BaseMarker.Describe(range) : null;

    /// <summary>What is shown out of what is loaded, so a narrowed list says what it hides.</summary>
    [ObservableProperty]
    public partial string LoadedText { get; private set; } = string.Empty;

    /// <summary>The path answered and has no history at all — never true before it answered.</summary>
    public bool IsEmpty => State == HistoryState.Ready && _loaded.Count == 0;

    public RevisionDiffPaneViewModel Diff { get; } = diff;

    /// <summary>
    /// Shows <paramref name="path"/>'s history from HEAD, and asks where this copy's BASE is at the
    /// same time. Anything still coming back for an earlier path is dropped on arrival.
    /// </summary>
    /// <param name="path">Absolute, inside a working copy — its root for the whole copy's history.</param>
    public async Task ShowAsync(string path, CancellationToken cancellationToken)
    {
        _showing?.Answers.Cancel();
        _showing?.Answers.Dispose();
        _showing = new Showing(
            path,
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        );
        var showing = _showing.Answers.Token;

        Path = path;
        _loaded.Clear();
        _cursor = HistoryCursor.Fresh;
        _isReadingPage = false;
        HasMore = false;
        IsLoadingMore = false;
        BaseRange = null;
        Message = null;
        SearchText = string.Empty;
        SelectedItem = null;
        Items.Clear();
        State = HistoryState.Loading;
        Resync();

        var baseRange = ReadBaseRangeAsync(path, showing);
        await ReadPageAsync(path, showing);
        await baseRange;
    }

    /// <summary>The list scrolled near its end: reads the next page, unless one is on its way or none is left.</summary>
    [RelayCommand]
    private Task LoadMoreAsync() =>
        _showing is { } shown && HasMore
            ? ReadPageAsync(shown.Path, shown.Answers.Token)
            : Task.CompletedTask;

    [RelayCommand]
    private Task ReloadAsync() =>
        Path is { } path ? ShowAsync(path, CancellationToken.None) : Task.CompletedTask;

    [RelayCommand]
    private void ReturnToChanges() => ReturnToChangesRequested?.Invoke(this, EventArgs.Empty);

    partial void OnSearchTextChanged(string value) => Resync();

    partial void OnSelectedItemChanged(RevisionListItem? value)
    {
        if (_isSyncing)
        {
            return;
        }

        SelectedRevision = value?.Row;
        var first = value?.Row.ChangedPaths.FirstOrDefault();

        // Two revisions can list an equal first path — the same file modified twice — and then
        // the setter sees no change, although the diff to show is another revision's.
        if (Equals(SelectedPath, first))
        {
            ShowDiffOf(first);
        }
        else
        {
            SelectedPath = first;
        }
    }

    partial void OnSelectedPathChanged(ChangedPathRow? value) => ShowDiffOf(value);

    private void ShowDiffOf(ChangedPathRow? value)
    {
        if (value is null || SelectedRevision is not { } revision || Path is not { } path)
        {
            Diff.Clear();
            return;
        }

        _ = Diff.SelectAsync(path, revision.Revision, value);
    }

    private async Task ReadPageAsync(string path, CancellationToken showing)
    {
        if (_isReadingPage || !_cursor.HasMore)
        {
            return;
        }

        _isReadingPage = true;
        IsLoadingMore = _loaded.Count > 0;
        try
        {
            var response = await history.ReadAsync(path, _cursor.Next(), PageSize, showing);
            if (!showing.IsCancellationRequested)
            {
                Take(response);
            }
        }
        catch (OperationCanceledException) when (showing.IsCancellationRequested)
        {
            // A newer ShowAsync owns the state now.
        }
        catch (DaemonUnreachableException unreachable)
        {
            if (!showing.IsCancellationRequested)
            {
                Fail(HistoryState.Unreachable, unreachable.Message);
            }
        }
        finally
        {
            if (!showing.IsCancellationRequested)
            {
                _isReadingPage = false;
                IsLoadingMore = false;
            }
        }
    }

    private void Take(DaemonResponse response)
    {
        switch (response)
        {
            case LogResponse log:
                _loaded.AddRange(
                    log.Revisions.Select(entry => RevisionRow.From(entry, clock.LocalTimeZone))
                );
                _cursor = _cursor.After(
                    [.. log.Revisions.Select(entry => entry.Revision)],
                    PageSize
                );
                HasMore = _cursor.HasMore;
                Message = null;
                State = HistoryState.Ready;
                Resync();
                break;

            case ErrorResponse error:
                Fail(HistoryState.Failed, error.Message);
                break;

            default:
                Fail(
                    HistoryState.Failed,
                    $"The daemon answered with {response.GetType().Name}, which is not a history."
                );
                break;
        }
    }

    /// <summary>A failure after the first page keeps what was read; see <see cref="HistoryState.Ready"/>.</summary>
    private void Fail(HistoryState state, string message)
    {
        Message = message;
        if (_loaded.Count == 0)
        {
            State = state;
        }
    }

    /// <summary>The marker is a nicety: a copy whose BASE cannot be read still shows its history.</summary>
    private async Task ReadBaseRangeAsync(string path, CancellationToken showing)
    {
        DaemonResponse response;
        try
        {
            response = await history.ReadBaseRangeAsync(path, showing);
        }
        catch (OperationCanceledException) when (showing.IsCancellationRequested)
        {
            return;
        }
        catch (DaemonUnreachableException)
        {
            return;
        }

        if (!showing.IsCancellationRequested && response is WorkingCopyRevisionResponse answer)
        {
            BaseRange = answer.Range;
            Resync();
        }
    }

    /// <summary>
    /// Brings <see cref="Items"/> in line with what is loaded, the search and the BASE range, then
    /// puts the selection back on its revision's newest item — or drops it with its diff, when the
    /// search has hidden it.
    /// </summary>
    private void Resync()
    {
        var shown = _loaded.Where(row => RevisionSearch.Matches(row, SearchText)).ToList();
        var selected = SelectedItem?.Row.Revision;
        RevisionListItem? listed;

        _isSyncing = true;
        try
        {
            RevisionListSynchronizer.Apply(Items, BaseMarker.Place(shown, BaseRange));
            listed = Items.FirstOrDefault(item => item.Row.Revision == selected);
            SelectedItem = listed;
        }
        finally
        {
            _isSyncing = false;
        }

        if (selected is not null && listed is null)
        {
            SelectedRevision = null;
            SelectedPath = null;
        }

        LoadedText =
            shown.Count == _loaded.Count
                ? $"{_loaded.Count} loaded"
                : $"{shown.Count} of {_loaded.Count} loaded match";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private sealed record Showing(string Path, CancellationTokenSource Answers);
}
