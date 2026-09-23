namespace Subverted.App.ViewModels;

/// <summary>What the diff pane can honestly say about the selected row.</summary>
public enum DiffPaneState
{
    NothingSelected,

    /// <summary>A diff was asked for and has not come back, and there is no earlier one of this row to show.</summary>
    Loading,

    /// <summary><see cref="DiffPaneViewModel.Document"/> holds at least one file.</summary>
    Ready,

    /// <summary>SVN had nothing to diff — an unversioned file, say. The message says why.</summary>
    NothingToShow,

    Unreachable,

    Failed,
}
