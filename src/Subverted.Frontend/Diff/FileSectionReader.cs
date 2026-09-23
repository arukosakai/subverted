namespace Subverted.Frontend.Diff;

/// <summary>
/// Reads everything <c>svn diff</c> printed under one <c>Index: X</c> header, up to the next header
/// naming a different path.
/// </summary>
internal static class FileSectionReader
{
    public const string Header = "Index: ";

    private const string BinaryMarker = "Cannot display: file marked as a binary type.";
    private const string MimeTypePrefix = "svn:mime-type = ";
    private const string WorkingCopyRoot = ".";

    /// <param name="headerLine">The <c>Index:</c> line the cursor is positioned on.</param>
    /// <param name="cursor">Left on the next section's header, or past the end.</param>
    /// <remarks>
    /// A binary file with a property change comes as two sections with the same header — the
    /// binary notice, then the properties — and is read here as one file, which it is.
    /// </remarks>
    public static FileDiff Read(string headerLine, LineCursor cursor)
    {
        cursor.Advance();

        var hunks = new List<Hunk>();
        var properties = new List<PropertyChange>();
        BinaryChange? binary = null;

        while (cursor.Current is { } line && !StartsAnotherFile(line, headerLine))
        {
            if (line == BinaryMarker)
            {
                binary = ReadBinary(cursor);
            }
            else if (line.StartsWith(PropertySectionReader.Header, StringComparison.Ordinal))
            {
                properties.AddRange(PropertySectionReader.Read(cursor));
            }
            else if (HunkRange.Parse(line, HunkRange.ContentFence) is { } range)
            {
                cursor.Advance();
                hunks.Add(HunkReader.Read(range, cursor));
            }
            else
            {
                cursor.Advance();
            }
        }

        return new FileDiff(PathOf(headerLine), ContentOf(binary, hunks, properties), properties);
    }

    private static bool StartsAnotherFile(string line, string headerLine) =>
        line.StartsWith(Header, StringComparison.Ordinal) && line != headerLine;

    /// <remarks>SVN spells paths with <c>/</c> on every platform and names the root <c>.</c>.</remarks>
    private static string PathOf(string headerLine)
    {
        var printed = headerLine[Header.Length..];
        return printed == WorkingCopyRoot ? string.Empty : printed;
    }

    private static BinaryChange ReadBinary(LineCursor cursor)
    {
        cursor.Advance();
        if (
            cursor.Current is not { } line
            || !line.StartsWith(MimeTypePrefix, StringComparison.Ordinal)
        )
        {
            return new BinaryChange(MimeType: null);
        }

        cursor.Advance();
        return new BinaryChange(line[MimeTypePrefix.Length..]);
    }

    /// <remarks>
    /// A section with nothing but properties is a property-only change. One with nothing at all is
    /// still a content change: an added empty file prints just its header.
    /// </remarks>
    private static FileContentChange? ContentOf(
        BinaryChange? binary,
        List<Hunk> hunks,
        List<PropertyChange> properties
    )
    {
        if (binary is not null)
        {
            return binary;
        }

        var onlyPropertiesChanged = hunks.Count == 0 && properties.Count > 0;
        return onlyPropertiesChanged ? null : new TextChange(hunks);
    }
}
