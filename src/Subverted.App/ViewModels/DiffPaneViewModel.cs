using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;
using Subverted.Frontend.Diff;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// The right-hand pane: the selected row's local changes, fetched fresh from the daemon. One
/// question is live at a time; asking another cancels it, and an answer to a cancelled question is
/// dropped, so a slow diff of the last row can never overwrite the current one.
/// </summary>
/// <remarks>Call it from the UI thread: its answers come back on the context that asked.</remarks>
public sealed partial class DiffPaneViewModel(
    IWorkingCopyDiff diffs,
    IFileSizeReader sizes,
    IFileLauncher launcher,
    TimeProvider clock
) : ObservableObject
{
    /// <summary>
    /// Holding an arrow key repeats every ~30 ms, so this sends one question for the row it stops
    /// on rather than one per row it passes, and is still under what reads as a delay.
    /// </summary>
    public static readonly TimeSpan SelectionDebounce = TimeSpan.FromMilliseconds(150);

    private CancellationTokenSource? _asking;
    private string? _path;

    [ObservableProperty]
    public partial DiffPaneState State { get; private set; } = DiffPaneState.NothingSelected;

    /// <summary>The row whose diff is shown, which carries the badge the pane's header draws.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInAppCommand))]
    public partial ChangeRow? Row { get; private set; }

    [ObservableProperty]
    public partial DiffDocument? Document { get; private set; }

    /// <summary>
    /// Why the pane is not showing a diff, when it is not — or, alongside <see cref="Document"/> in
    /// <see cref="DiffPaneState.Ready"/>, why the diff shown may be out of date.
    /// </summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>The file's size on disk, for the binary card; <c>null</c> when it is not there.</summary>
    [ObservableProperty]
    public partial long? SizeInBytes { get; private set; }

    /// <summary>
    /// The person picked a row: it shows as loading at once and is asked about once the selection
    /// has held for <see cref="SelectionDebounce"/>.
    /// </summary>
    /// <param name="path">The row's absolute path; see <see cref="DiffTarget"/>.</param>
    /// <returns>Completes when the answer is shown, or when a later call superseded this one.</returns>
    public async Task SelectAsync(ChangeRow row, string path)
    {
        var asking = Restart(row, path);
        if (SvnHasNoDiffFor(row))
        {
            ShowNothing(row);
            return;
        }

        Document = null;
        SizeInBytes = null;
        Message = null;
        State = DiffPaneState.Loading;
        try
        {
            await Task.Delay(SelectionDebounce, clock, asking);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await FetchAsync(row, path, asking);
    }

    /// <summary>
    /// The shown row changed under it, so it is asked about again straight away. What is on screen
    /// stays until the answer replaces it — a refetch is never a blank or a spinner.
    /// </summary>
    public async Task RefetchAsync(ChangeRow row, string path)
    {
        var asking = Restart(row, path);
        if (SvnHasNoDiffFor(row))
        {
            ShowNothing(row);
            return;
        }

        await FetchAsync(row, path, asking);
    }

    /// <summary>Nothing is selected: shows that, and drops any question still in flight.</summary>
    public void Clear()
    {
        Cancel();
        _path = null;
        Row = null;
        Document = null;
        SizeInBytes = null;
        Message = null;
        State = DiffPaneState.NothingSelected;
    }

    [RelayCommand(CanExecute = nameof(CanOpenInApp))]
    private Task OpenInAppAsync() =>
        _path is { } path ? launcher.OpenAsync(path) : Task.CompletedTask;

    private bool CanOpenInApp() => Row is not null;

    /// <summary><c>svn diff</c> refuses an unversioned path outright (E150000), so it is not asked.</summary>
    private static bool SvnHasNoDiffFor(ChangeRow row) => row.Badge.Tone == ChangeTone.Unversioned;

    private CancellationToken Restart(ChangeRow row, string path)
    {
        Cancel();
        _asking = new CancellationTokenSource();
        _path = path;
        Row = row;
        return _asking.Token;
    }

    private void Cancel()
    {
        if (_asking is { } asking)
        {
            _asking = null;
            asking.Cancel();
            asking.Dispose();
        }
    }

    private async Task FetchAsync(ChangeRow row, string path, CancellationToken asking)
    {
        DaemonResponse response;
        try
        {
            response = await diffs.ReadAsync(path, asking);
        }
        catch (OperationCanceledException) when (asking.IsCancellationRequested)
        {
            return;
        }
        catch (DaemonUnreachableException unreachable)
        {
            if (!asking.IsCancellationRequested)
            {
                ShowProblem(DiffPaneState.Unreachable, unreachable.Message);
            }

            return;
        }

        if (asking.IsCancellationRequested)
        {
            return;
        }

        switch (response)
        {
            case DiffResponse diff:
                ShowDiff(row, path, diff);
                break;

            case ErrorResponse error:
                ShowProblem(DiffPaneState.Failed, error.Message);
                break;

            default:
                ShowProblem(
                    DiffPaneState.Failed,
                    $"The daemon answered with {response.GetType().Name}, which is not a diff."
                );
                break;
        }
    }

    private void ShowDiff(ChangeRow row, string path, DiffResponse diff)
    {
        // The protocol promises empty text for "nothing changed"; that needs no parse.
        var document =
            diff.UnifiedDiff.Length == 0
                ? DiffDocument.Empty
                : UnifiedDiffParser.Parse(diff.UnifiedDiff);
        if (document.Files.Count == 0)
        {
            ShowNothing(row);
            return;
        }

        Document = document;
        SizeInBytes = document.Files.Any(file => file.Content is BinaryChange)
            ? sizes.ReadSize(path)
            : null;
        Message = null;
        State = DiffPaneState.Ready;
    }

    private void ShowNothing(ChangeRow row)
    {
        Document = null;
        SizeInBytes = null;
        Message = EmptyDiffMessage.For(row);
        State = DiffPaneState.NothingToShow;
    }

    /// <summary>An earlier diff of this row stays on screen, marked by the message; see <see cref="DiffPaneState.Ready"/>.</summary>
    private void ShowProblem(DiffPaneState state, string message)
    {
        Message = message;
        if (Document is null)
        {
            State = state;
        }
    }
}
