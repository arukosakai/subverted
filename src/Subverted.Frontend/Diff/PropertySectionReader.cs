namespace Subverted.Frontend.Diff;

/// <summary>
/// Reads SVN's <c>Property changes on: X</c> section: an underline, then per property an
/// <c>Added:</c>, <c>Modified:</c> or <c>Deleted:</c> header and its <c>## … ##</c> hunks.
/// </summary>
internal static class PropertySectionReader
{
    public const string Header = "Property changes on: ";

    private const string MergeSummaryIndent = "   ";

    /// <param name="cursor">Positioned on the section's header; left on the first line after it.</param>
    public static IReadOnlyList<PropertyChange> Read(LineCursor cursor)
    {
        cursor.Advance();
        var changes = new List<PropertyChange>();

        while (cursor.Current is { } line)
        {
            if (HeaderOf(line) is { } header)
            {
                cursor.Advance();
                changes.Add(ReadProperty(header.Name, header.Kind, cursor));
            }
            else if (line.StartsWith('_'))
            {
                cursor.Advance();
            }
            else
            {
                break;
            }
        }

        return changes;
    }

    private static (string Name, PropertyChangeKind Kind)? HeaderOf(string line)
    {
        var colon = line.IndexOf(": ", StringComparison.Ordinal);
        if (colon < 0)
        {
            return null;
        }

        PropertyChangeKind? kind = line[..colon] switch
        {
            "Added" => PropertyChangeKind.Added,
            "Modified" => PropertyChangeKind.Modified,
            "Deleted" => PropertyChangeKind.Deleted,
            _ => null,
        };

        return kind is { } known ? (line[(colon + 2)..], known) : null;
    }

    /// <remarks>
    /// <c>svn:mergeinfo</c> is the one property SVN describes in prose instead of diffing — indented
    /// <c>Merged …</c> lines in place of hunks — so both shapes are read under any header.
    /// </remarks>
    private static PropertyChange ReadProperty(
        string name,
        PropertyChangeKind kind,
        LineCursor cursor
    )
    {
        var hunks = new List<Hunk>();
        var mergeSummary = new List<string>();

        while (cursor.Current is { } line)
        {
            if (HunkRange.Parse(line, HunkRange.PropertyFence) is { } range)
            {
                cursor.Advance();
                hunks.Add(HunkReader.Read(range, cursor));
            }
            else if (line.StartsWith(MergeSummaryIndent, StringComparison.Ordinal))
            {
                cursor.Advance();
                mergeSummary.Add(line[MergeSummaryIndent.Length..]);
            }
            else
            {
                break;
            }
        }

        return new PropertyChange(name, kind, hunks) { MergeSummary = mergeSummary };
    }
}
