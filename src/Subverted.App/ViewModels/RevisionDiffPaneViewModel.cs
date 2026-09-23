using CommunityToolkit.Mvvm.ComponentModel;
using Subverted.App.Presentation;
using Subverted.Frontend.Diff;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// The History view's diff: what the selected revision did to the selected path. One question is
/// live at a time; asking another cancels it and a late answer to the old one is dropped, as in
/// <see cref="DiffPaneViewModel"/>.
/// </summary>
/// <remarks>
/// Call it from the UI thread. A committed revision never changes, so there is no refetch here —
/// only the selection moves.
/// </remarks>
public sealed partial class RevisionDiffPaneViewModel(IRevisionDiff diffs, TimeProvider clock)
    : ObservableObject
{
    private CancellationTokenSource? _asking;

    [ObservableProperty]
    public partial DiffPaneState State { get; private set; } = DiffPaneState.NothingSelected;

    /// <summary>The path whose change is shown, which carries the badge the header draws.</summary>
    [ObservableProperty]
    public partial ChangedPathRow? Path { get; private set; }

    [ObservableProperty]
    public partial long? Revision { get; private set; }

    [ObservableProperty]
    public partial DiffDocument? Document { get; private set; }

    /// <summary>Why the pane is not showing a diff, when it is not.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>
    /// A path was picked: it shows as loading at once and is asked about once the selection has
    /// held for <see cref="DiffPaneViewModel.SelectionDebounce"/>.
    /// </summary>
    /// <returns>Completes when the answer is shown, or when a later call superseded this one.</returns>
    public async Task SelectAsync(string workingCopyPath, long revision, ChangedPathRow path)
    {
        Cancel();
        _asking = new CancellationTokenSource();
        var asking = _asking.Token;
        Path = path;
        Revision = revision;
        Document = null;
        Message = null;
        State = DiffPaneState.Loading;
        try
        {
            await Task.Delay(DiffPaneViewModel.SelectionDebounce, clock, asking);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await FetchAsync(workingCopyPath, revision, path, asking);
    }

    /// <summary>Nothing is selected: shows that, and drops any question still in flight.</summary>
    public void Clear()
    {
        Cancel();
        Path = null;
        Revision = null;
        Document = null;
        Message = null;
        State = DiffPaneState.NothingSelected;
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

    private async Task FetchAsync(
        string workingCopyPath,
        long revision,
        ChangedPathRow path,
        CancellationToken asking
    )
    {
        DaemonResponse response;
        try
        {
            response = await diffs.ReadAsync(workingCopyPath, path.Path, revision, asking);
        }
        catch (OperationCanceledException) when (asking.IsCancellationRequested)
        {
            return;
        }
        catch (DaemonUnreachableException unreachable)
        {
            if (!asking.IsCancellationRequested)
            {
                Show(DiffPaneState.Unreachable, unreachable.Message);
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
                ShowDiff(path, diff);
                break;

            case ErrorResponse error:
                Show(DiffPaneState.Failed, error.Message);
                break;

            default:
                Show(
                    DiffPaneState.Failed,
                    $"The daemon answered with {response.GetType().Name}, which is not a diff."
                );
                break;
        }
    }

    private void ShowDiff(ChangedPathRow path, DiffResponse diff)
    {
        var document = UnifiedDiffParser.Parse(diff.UnifiedDiff);
        if (document.Files.Count == 0)
        {
            Show(DiffPaneState.NothingToShow, RevisionDiffEmptyMessage.For(path));
            return;
        }

        Document = document;
        State = DiffPaneState.Ready;
    }

    private void Show(DiffPaneState state, string message)
    {
        Message = message;
        State = state;
    }
}
