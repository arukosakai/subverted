namespace Subverted.App.ViewModels;

/// <summary>What the working-copy view can honestly say, which is not always a listing.</summary>
public enum WorkingCopyState
{
    /// <summary>Nothing has come back yet. A cold 100k-node scan takes about a second.</summary>
    Loading,

    Ready,

    /// <summary>The folder is not inside a working copy at all.</summary>
    NotAWorkingCopy,

    /// <summary>No daemon answered, and starting one did not help.</summary>
    Unreachable,

    /// <summary>The daemon answered with a failure; the message says which.</summary>
    Failed,
}
