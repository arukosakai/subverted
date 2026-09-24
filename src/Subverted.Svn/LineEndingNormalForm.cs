namespace Subverted.Svn;

/// <summary>
/// Puts a file's text into the form its pristine is stored in, so the two compare as
/// <c>svn diff</c> compares them. Only line endings: a file with <c>svn:keywords</c> never gets here.
/// </summary>
/// <remarks>
/// Measured on 1.8.15 for every style: a working file whose endings are all one kind — any kind,
/// not only the style's own — is compared with each ending replaced by the style's, and one that
/// mixes two kinds makes <c>svn diff</c> fail with <c>E135000</c>, inconsistent line endings.
/// </remarks>
internal static class LineEndingNormalForm
{
    /// <returns>
    /// The text in normal form — the same array under <see cref="LineEndingStyle.AsCommitted"/> —
    /// or <see langword="null"/> when its endings are inconsistent, since SVN refuses such a file.
    /// </returns>
    public static byte[]? Of(byte[] text, LineEndingStyle style)
    {
        if (style == LineEndingStyle.AsCommitted)
        {
            return text;
        }

        var lines = TextLines.Split(text);
        ReadOnlySpan<byte> seen = default;
        var seenAny = false;
        foreach (var line in lines)
        {
            if (!line.HasEnding)
            {
                continue;
            }

            var ending = EndingOf(text, line);
            if (seenAny && !ending.SequenceEqual(seen))
            {
                return null;
            }

            seen = ending;
            seenAny = true;
        }

        var normal = NormalEnding(style);
        using var output = new MemoryStream(text.Length);
        foreach (var line in lines)
        {
            var ending = line.HasEnding ? EndingOf(text, line).Length : 0;
            output.Write(text, line.Start, line.Length - ending);
            if (line.HasEnding)
            {
                output.Write(normal);
            }
        }

        return output.ToArray();
    }

    private static ReadOnlySpan<byte> EndingOf(byte[] text, TextLine line)
    {
        var last = line.Start + line.Length - 1;
        var isCrLf = text[last] == '\n' && line.Length > 1 && text[last - 1] == '\r';
        return isCrLf ? text.AsSpan(last - 1, 2) : text.AsSpan(last, 1);
    }

    private static ReadOnlySpan<byte> NormalEnding(LineEndingStyle style) =>
        style switch
        {
            LineEndingStyle.CrLf => "\r\n"u8,
            LineEndingStyle.Cr => "\r"u8,
            _ => "\n"u8,
        };
}
