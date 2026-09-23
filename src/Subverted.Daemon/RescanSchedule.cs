namespace Subverted.Daemon;

/// <summary>
/// Decides when a stale working copy is due for a background rescan: once it has gone quiet, not
/// the moment it changes. An asset import writes for a minute, and rescanning after every file it
/// writes would cost more than answering cold ever did.
/// </summary>
/// <remarks>
/// Stateful — it remembers the generation it last saw per root — but it touches nothing outside
/// itself and reads no clock, which is what lets the settle rule be tested without waiting for
/// real seconds to pass.
/// </remarks>
public sealed class RescanSchedule(TimeSpan settle)
{
    private readonly Dictionary<string, Seen> _lastSeen = new(
        StringComparer.FromComparison(ContainingRoot.PlatformComparison)
    );

    /// <param name="generation">The session's change counter. Moving means it is still being written to.</param>
    /// <param name="isStale">False when the held scan still stands, and there is nothing to do.</param>
    /// <returns>
    /// True only on a call where the root is stale and its generation has held still for the whole
    /// settle period. The first sighting of any generation is never due: it starts the clock.
    /// </returns>
    public bool IsDue(string root, long generation, bool isStale, DateTimeOffset now)
    {
        if (!_lastSeen.TryGetValue(root, out var seen) || seen.Generation != generation)
        {
            _lastSeen[root] = new Seen(generation, now);
            return false;
        }

        return isStale && now - seen.At >= settle;
    }

    private sealed record Seen(long Generation, DateTimeOffset At);
}
