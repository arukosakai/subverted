using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Decides whether a node's working properties have diverged from the set SVN recorded for it.
/// Pure, like <see cref="NodeStatusResolver"/>: both property sets come out of wc.db, so this
/// rule never touches the filesystem and is exhaustively testable on stored blobs alone.
/// </summary>
internal static class PropertyStatusResolver
{
    /// <param name="opDepth">0 for a pristine BASE node; higher for a local add, copy or delete.</param>
    /// <param name="isWithinCopy">See <see cref="WcDbRow.IsWithinCopy"/>.</param>
    /// <param name="workingProperties">
    /// <c>ACTUAL_NODE.properties</c>. <see langword="null"/> is SVN saying "no local property
    /// change recorded", not "this node has no properties" — an empty skel means the latter.
    /// </param>
    /// <param name="pristineProperties"><c>NODES.properties</c> at the node's highest op_depth.</param>
    /// <returns>
    /// <see cref="PropertyStatus.Modified"/> when the sets differ or either blob is unparseable.
    /// </returns>
    public static PropertyStatus Resolve(
        int opDepth,
        bool isWithinCopy,
        byte[]? workingProperties,
        byte[]? pristineProperties
    )
    {
        // `svn status` leaves the property column blank on added and copied nodes even when their
        // properties differ from the copy source's: the add is what carries them. A node a copy
        // brought along is not an add of its own, and compares like BASE.
        if (opDepth > 0 && !isWithinCopy)
        {
            return PropertyStatus.Unmodified;
        }

        if (workingProperties is null)
        {
            return PropertyStatus.Unmodified;
        }

        var working = SvnPropertySkel.Parse(workingProperties);
        var pristine = SvnPropertySkel.Parse(pristineProperties);

        // Declining to guess costs a spurious ' M'; guessing "unmodified" hides work from a commit.
        if (working is null || pristine is null)
        {
            return PropertyStatus.Modified;
        }

        return AreEquivalent(working, pristine)
            ? PropertyStatus.Unmodified
            : PropertyStatus.Modified;
    }

    /// <summary>
    /// Compares by name and value rather than by stored order. SVN happens to write these
    /// alphabetically, but nothing in the format promises it.
    /// </summary>
    private static bool AreEquivalent(
        IReadOnlyList<SvnProperty> working,
        IReadOnlyList<SvnProperty> pristine
    )
    {
        if (working.Count != pristine.Count)
        {
            return false;
        }

        var byName = new Dictionary<string, string>(working.Count, StringComparer.Ordinal);
        foreach (var property in working)
        {
            byName[property.Name] = property.Value;
        }

        foreach (var property in pristine)
        {
            if (!byName.TryGetValue(property.Name, out var value) || value != property.Value)
            {
                return false;
            }
        }

        return true;
    }
}
