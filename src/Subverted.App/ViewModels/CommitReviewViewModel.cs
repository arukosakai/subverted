using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>
/// The review window: what a commit would send, one file's diff at a time, and the same message
/// and Commit as the strip. It keeps no ticks, message or commit of its own — those are the
/// table's and the composer's, so whichever the person used, the other already shows it.
/// </summary>
/// <remarks>
/// Lists what was offered when it opened; a line unticked here stays listed so it can be ticked
/// again. Dispose it once closed: it stops following the composer and drops its diff.
/// </remarks>
public sealed partial class CommitReviewViewModel : ObservableObject, IDisposable
{
    private readonly ICommitTicks _ticks;
    private readonly string _root;

    /// <param name="diff">The review's own pane, so the table's diff stays on the table's line.</param>
    /// <param name="root">The working-copy root the offered paths are relative to.</param>
    public CommitReviewViewModel(
        CommitComposerViewModel composer,
        DiffPaneViewModel diff,
        ICommitTicks ticks,
        string root
    )
    {
        Composer = composer;
        Diff = diff;
        _ticks = ticks;
        _root = root;
        foreach (var row in composer.Selection.Sent)
        {
            Changes.Add(new ChangeListEntry(ChangeListItem.Flat(row)));
        }

        ShowTicks();
        composer.PropertyChanged += OnComposerChanged;
        composer.Attempted += OnAttempted;
        SelectedChange = Changes.FirstOrDefault();
    }

    public CommitComposerViewModel Composer { get; }

    public DiffPaneViewModel Diff { get; }

    public ObservableCollection<ChangeListEntry> Changes { get; } = [];

    /// <summary>The line whose diff is shown.</summary>
    [ObservableProperty]
    public partial ChangeListEntry? SelectedChange { get; set; }

    /// <summary>The review is done with — committed or cancelled — and whatever shows it should go.</summary>
    public event Action? CloseRequested;

    partial void OnSelectedChangeChanged(ChangeListEntry? value)
    {
        if (value?.Row is not { } row)
        {
            Diff.Clear();
            return;
        }

        _ = Diff.SelectAsync(row, DiffTarget.PathOf(_root, row.RelPath));
    }

    /// <remarks>The marks follow from the composer's new selection, which every toggle brings.</remarks>
    [RelayCommand(CanExecute = nameof(CanTick))]
    private void ToggleTick(ChangeListEntry? entry) => _ticks.Toggle(entry!.Row!.RelPath);

    private static bool CanTick(ChangeListEntry? entry) => entry?.IsTickable == true;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    private void OnComposerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommitComposerViewModel.Selection))
        {
            ShowTicks();
        }
    }

    /// <summary>Only a commit the daemon took closes it; anything else stays, notice and all.</summary>
    private void OnAttempted(CommitAttempt attempt)
    {
        if (attempt.Notice.Kind == NoticeKind.Succeeded)
        {
            CloseRequested?.Invoke();
        }
    }

    /// <summary>The same marks as the table's: a line a folder above it now decides is not a choice.</summary>
    private void ShowTicks()
    {
        var decided = Composer.Selection.DecidedByFolder;
        foreach (var entry in Changes)
        {
            entry.IsTicked = _ticks.IsTicked(entry.Key);
            entry.IsDecidedByFolder = decided.Contains(entry.Key);
        }

        ToggleTickCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        Composer.PropertyChanged -= OnComposerChanged;
        Composer.Attempted -= OnAttempted;
        Diff.Clear();
    }
}
