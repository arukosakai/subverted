namespace Subverted.Core;

/// <summary>
/// The BASE revisions a working copy's nodes are at — <c>svnversion</c>'s <c>3:5</c>. A copy that
/// was updated in parts has no single BASE, so this is a range and only sometimes a point.
/// </summary>
/// <param name="Lowest">The oldest BASE any node is at.</param>
/// <param name="Highest">The newest; equal to <paramref name="Lowest"/> unless the copy is mixed.</param>
public sealed record BaseRevisionRange(long Lowest, long Highest)
{
    /// <summary>Some nodes were updated further than others.</summary>
    public bool IsMixed => Lowest != Highest;
}
