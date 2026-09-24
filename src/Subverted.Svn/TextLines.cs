namespace Subverted.Svn;

/// <summary>
/// Cuts a file into lines where <c>svn diff</c> does: at <c>\r\n</c>, a lone <c>\r</c> and a lone
/// <c>\n</c>. Measured on 1.8.15: <c>a\rb\n</c> is two lines, and the ending is part of the line, so
/// <c>b\n</c> and <c>b\r\n</c> differ.
/// </summary>
internal static class TextLines
{
    /// <returns>Every line in order; none for an empty text, and no empty line after a final ending.</returns>
    public static TextLine[] Split(ReadOnlySpan<byte> text)
    {
        var lines = new List<TextLine>();
        var start = 0;
        while (start < text.Length)
        {
            var ending = text[start..].IndexOfAny((byte)'\r', (byte)'\n');
            if (ending < 0)
            {
                lines.Add(new TextLine(start, text.Length - start, HasEnding: false));
                break;
            }

            var end = start + ending + 1;
            var isCrLf = text[end - 1] == '\r' && end < text.Length && text[end] == '\n';
            if (isCrLf)
            {
                end++;
            }

            lines.Add(new TextLine(start, end - start, HasEnding: true));
            start = end;
        }

        return [.. lines];
    }
}
