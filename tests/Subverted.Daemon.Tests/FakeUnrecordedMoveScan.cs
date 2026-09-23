using Subverted.Core;

namespace Subverted.Daemon.Tests;

/// <summary>
/// Pairs a test can change between scans, recording what it was handed. The entries matter as much
/// as the count: the session must pass the listing it just produced, not one it kept from before.
/// </summary>
internal sealed class FakeUnrecordedMoveScan : IUnrecordedMoveScan
{
    public int Reads { get; private set; }

    public IReadOnlyList<UnrecordedMove> Moves { get; set; } = [];

    /// <summary>The listing handed to the most recent call, or null before the first one.</summary>
    public IReadOnlyList<WorkingCopyEntry>? LastEntries { get; private set; }

    public IReadOnlyList<UnrecordedMove> FindUnrecordedMoves(
        IReadOnlyList<WorkingCopyEntry> entries
    )
    {
        Reads++;
        LastEntries = entries;
        return Moves;
    }
}
