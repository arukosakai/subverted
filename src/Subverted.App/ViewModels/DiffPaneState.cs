namespace Subverted.App.ViewModels;

/// <summary>What the diff pane can honestly say about the selected row.</summary>
public enum DiffPaneState
{
    NothingSelected,

    /// <summary>A diff was asked for and has not come back, and there is no earlier one of this row to show.</summary>
    Loading,

    /// <summary>
    /// <see cref="DiffPaneViewModel.Document"/> holds at least one file. With a
    /// <see cref="DiffPaneViewModel.Message"/> as well, asking again failed and this is the last
    /// diff that could be read — kept, as the listing is, rather than blanked.
    /// </summary>
    Ready,

    /// <summary>SVN had nothing to diff — an unversioned file, say. The message says why.</summary>
    NothingToShow,

    /// <summary>No daemon answered, and there is no earlier diff of this row to fall back on.</summary>
    Unreachable,

    /// <summary>The daemon answered with a failure, and there is no earlier diff of this row to fall back on.</summary>
    Failed,
}
