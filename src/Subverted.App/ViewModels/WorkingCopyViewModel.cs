using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Subverted.App.Presentation;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>One working copy as the window shows it: what changed, and whether that answer is real.</summary>
/// <param name="diff">Shows whichever row is selected; this view model tells it when that changes.</param>
public sealed partial class WorkingCopyViewModel(
    string path,
    IWorkingCopyStatus status,
    DiffPaneViewModel diff
) : ObservableObject
{
    /// <summary>
    /// The record the diff was last asked for. A resync replaces a changed row with a new record,
    /// which the list control drops from its selection; this one's path is what finds it again.
    /// </summary>
    private ChangeRow? _followed;

    private bool _isResyncing;

    /// <summary>The path the person opened, which may be anywhere inside the working copy.</summary>
    public string Path { get; } = path;

    public ObservableCollection<ChangeRow> Changes { get; } = [];

    /// <summary>
    /// The row the person picked. The once-a-second resync re-points it at the row's newest record
    /// rather than losing it, and that is not treated as a new pick.
    /// </summary>
    [ObservableProperty]
    public partial ChangeRow? SelectedChange { get; set; }

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
        var rows = listing
            .Entries.Select(ChangeRow.From)
            .OrderBy(row => row.RelPath, StringComparer.Ordinal)
            .ToList();

        RootPath = listing.Info.RootPath;
        Resync(rows);
        RepositoryRoot = listing.Info.RepositoryRoot;
        Name = FolderName.Of(listing.Info.RootPath);
        Summary = ChangeSummary.Of(rows);
        Message = null;
        State = WorkingCopyState.Ready;
        RaiseDerived();
    }

    partial void OnSelectedChangeChanged(ChangeRow? value)
    {
        if (_isResyncing)
        {
            return;
        }

        _followed = value;
        if (value is null)
        {
            Diff.Clear();
            return;
        }

        _ = Diff.SelectAsync(value, DiffTarget.PathOf(Location, value.RelPath));
    }

    /// <summary>
    /// Merges the fresh rows in, then puts the selection back on its path's newest record and asks
    /// for its diff again if that record says it changed. A row that left the listing — committed
    /// or reverted elsewhere — takes the selection with it.
    /// </summary>
    private void Resync(IReadOnlyList<ChangeRow> rows)
    {
        var shownFor = _followed;
        ChangeRow? listed;

        // The list control writes its dropped selection back through the binding mid-merge.
        _isResyncing = true;
        try
        {
            ChangeListSynchronizer.Apply(Changes, rows);
            listed = Changes.FirstOrDefault(row => row.RelPath == shownFor?.RelPath);
            SelectedChange = listed;
        }
        finally
        {
            _isResyncing = false;
        }

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
            _ = Diff.RefetchAsync(listed, DiffTarget.PathOf(Location, listed.RelPath));
        }
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
