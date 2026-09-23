namespace Subverted.Protocol;

/// <summary>
/// How much the daemon's in-memory index can be trusted to match disk. Reported rather than
/// hidden: a watcher that has silently stopped noticing changes is the failure mode that makes a
/// status cache worse than no cache at all.
/// </summary>
public enum WatcherState
{
    /// <summary>Every change on disk is being seen. The index is current.</summary>
    Healthy,

    /// <summary>Events were dropped and a rescan is in flight. Answers come from a fresh scan meanwhile.</summary>
    Recovering,

    /// <summary>
    /// No watcher at all — the platform refused one, typically an exhausted inotify watch limit.
    /// Every request rescans, which is slow and correct rather than fast and wrong.
    /// </summary>
    Unavailable,
}
