namespace Subverted.Svn;

/// <summary>
/// Answers the one question the status fast path asks of a node's properties: is the working file
/// a translation of its pristine, or a byte-for-byte copy of it?
/// </summary>
internal static class SvnProperties
{
    // Only these three change what lands on disk. svn:needs-lock, svn:mime-type and svn:executable
    // are everywhere on game assets, and must not cost those nodes their checksum fast path.
    private static readonly HashSet<string> TranslatingProperties = new(StringComparer.Ordinal)
    {
        "svn:eol-style",
        "svn:keywords",
        "svn:special",
    };

    /// <summary>
    /// <see langword="true"/> when the working file cannot be compared to its pristine by hash —
    /// either a translating property is set, or the skel did not parse and we decline to guess.
    /// </summary>
    /// <param name="properties">Raw <c>properties</c> blob; <see langword="null"/> when unrecorded.</param>
    public static bool IsTranslated(byte[]? properties)
    {
        if (SvnPropertySkel.Parse(properties) is not { } parsed)
        {
            return true;
        }

        foreach (var property in parsed)
        {
            if (TranslatingProperties.Contains(property.Name))
            {
                return true;
            }
        }

        return false;
    }
}
