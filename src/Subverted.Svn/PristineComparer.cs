using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Settles nodes the metadata fast path could not decide, by hashing the working file and
/// comparing against the checksum wc.db recorded at checkout.
/// </summary>
/// <remarks>
/// Holds no state, so one instance serves every thread at once — which is what lets
/// <see cref="WorkingCopyScanner"/> hash a whole working copy's worth of nodes in parallel. Adding
/// a field here would take that away silently.
/// </remarks>
internal sealed class PristineComparer
{
    /// <summary>
    /// Returns <see cref="NodeStatus.NeedsPristineCompare"/> when the answer is still unknowable —
    /// never a guess. Callers may surface that as "unknown" or fall back to <c>svn status</c>.
    /// </summary>
    public NodeStatus Compare(WcDbRow row, string absolutePath)
    {
        // A translated working file will not hash to the recorded checksum, so calling it modified
        // would flag clean files. Undecided is the only honest answer.
        if (row.IsTranslated)
        {
            return NodeStatus.NeedsPristineCompare;
        }

        if (SvnChecksum.TryParseSha1(row.Checksum) is not { } recorded)
        {
            return NodeStatus.NeedsPristineCompare;
        }

        var actual = WorkingFileDigest.TryCompute(absolutePath);
        if (actual is null)
        {
            return NodeStatus.NeedsPristineCompare;
        }

        return string.Equals(actual, recorded, StringComparison.Ordinal)
            ? NodeStatus.Unmodified
            : NodeStatus.Modified;
    }
}
