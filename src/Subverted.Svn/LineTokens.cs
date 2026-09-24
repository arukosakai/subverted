namespace Subverted.Svn;

/// <summary>
/// Gives every distinct line of two texts a number, so the search compares integers rather than
/// bytes. Equal numbers mean byte-for-byte equal lines, endings included.
/// </summary>
internal static class LineTokens
{
    public static (int[] Old, int[] New) Of(
        byte[] oldText,
        TextLine[] oldLines,
        byte[] newText,
        TextLine[] newLines
    )
    {
        var numbers = new Dictionary<ReadOnlyMemory<byte>, int>(
            oldLines.Length + newLines.Length,
            BytesComparer.Instance
        );

        return (Number(oldText, oldLines, numbers), Number(newText, newLines, numbers));
    }

    private static int[] Number(
        byte[] text,
        TextLine[] lines,
        Dictionary<ReadOnlyMemory<byte>, int> numbers
    )
    {
        var tokens = new int[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = text.AsMemory(lines[i].Start, lines[i].Length);
            if (!numbers.TryGetValue(line, out var number))
            {
                number = numbers.Count;
                numbers.Add(line, number);
            }

            tokens[i] = number;
        }

        return tokens;
    }

    private sealed class BytesComparer : IEqualityComparer<ReadOnlyMemory<byte>>
    {
        public static readonly BytesComparer Instance = new();

        public bool Equals(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y) =>
            x.Span.SequenceEqual(y.Span);

        public int GetHashCode(ReadOnlyMemory<byte> bytes)
        {
            var hash = new HashCode();
            hash.AddBytes(bytes.Span);
            return hash.ToHashCode();
        }
    }
}
