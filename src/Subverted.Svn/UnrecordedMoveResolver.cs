using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Pairs nodes that went missing with unversioned files carrying their content, which is how a
/// rename done outside SVN is recognised as one. Pure and I/O-free: the hashing happens elsewhere,
/// so every pairing rule is testable without a working copy.
/// </summary>
internal static class UnrecordedMoveResolver
{
    /// <param name="missing">Versioned files absent from disk, fingerprinted by the checksum wc.db recorded.</param>
    /// <param name="arrived">Unversioned files present on disk, fingerprinted by hashing them.</param>
    /// <returns>The pairs, ordered by the path that went missing. Empty when nothing pairs.</returns>
    public static IReadOnlyList<UnrecordedMove> Pair(
        IReadOnlyList<FingerprintedNode> missing,
        IReadOnlyList<FingerprintedNode> arrived
    )
    {
        if (missing.Count == 0 || arrived.Count == 0)
        {
            return [];
        }

        var destinations = SoleHolderOfEachDigest(arrived);
        return
        [
            .. SoleHolderOfEachDigest(missing)
                .Where(source => destinations.ContainsKey(source.Key))
                .Select(source => new UnrecordedMove(source.Value, destinations[source.Key]))
                .OrderBy(move => move.FromRelPath, StringComparer.Ordinal),
        ];
    }

    /// <returns>
    /// Digest to the one node carrying it. A digest two nodes share is left out on both sides:
    /// duplicate content makes which file became which a guess, and a wrong guess here tells someone
    /// their asset moved somewhere it did not.
    /// </returns>
    private static Dictionary<string, string> SoleHolderOfEachDigest(
        IReadOnlyList<FingerprintedNode> nodes
    ) =>
        nodes
            .GroupBy(node => node.Sha1, StringComparer.Ordinal)
            .Where(sharing => sharing.Count() == 1)
            .ToDictionary(
                sharing => sharing.Key,
                sharing => sharing.First().RelPath,
                StringComparer.Ordinal
            );
}
