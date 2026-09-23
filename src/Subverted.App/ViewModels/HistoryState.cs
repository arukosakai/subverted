namespace Subverted.App.ViewModels;

/// <summary>What the History list can honestly say about the path it was asked to show.</summary>
public enum HistoryState
{
    /// <summary>No path has been asked about yet.</summary>
    NothingShown,

    /// <summary>The first page was asked for and has not come back.</summary>
    Loading,

    /// <summary>
    /// At least one page came back. With a <see cref="HistoryViewModel.Message"/> as well, a later
    /// page failed; what was read stays on screen.
    /// </summary>
    Ready,

    /// <summary>No daemon answered the first page.</summary>
    Unreachable,

    /// <summary>The daemon answered the first page with a failure — the server, most often.</summary>
    Failed,
}
