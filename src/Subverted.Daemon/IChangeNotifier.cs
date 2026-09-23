namespace Subverted.Daemon;

/// <summary>
/// Tells a session that something under its root moved, and where.
/// </summary>
/// <remarks>
/// The path is a hint for re-resolving one node instead of the tree; it is never load-bearing for
/// correctness. Every event still means "the index no longer stands", and a notifier that cannot
/// name the path says <see cref="WorkingCopyChange.Unknown"/> rather than staying quiet — which is
/// what keeps D5's overflow rule intact.
/// </remarks>
public interface IChangeNotifier : IDisposable
{
    event Action<WorkingCopyChange>? Changed;

    /// <summary>
    /// False when nothing is actually being watched — the platform refused, or the watch was lost
    /// and could not be re-established. A session that is not watched can never call itself warm.
    /// </summary>
    bool IsWatching { get; }
}
