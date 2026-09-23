namespace Subverted.Frontend.Diff;

/// <summary>
/// Splits <c>svn diff</c> output into lines the way SVN's own diff tokenises a file: <c>\r\n</c>,
/// a lone <c>\r</c> and a lone <c>\n</c> each end a line. Splitting on <c>\n</c> alone would miscount
/// every hunk of a file with classic-Mac line endings, whose lines SVN prints ending in <c>\r</c>.
/// </summary>
internal static class DiffLines
{
    /// <returns>The lines without their endings; a final line ending does not start an empty line.</returns>
    public static IReadOnlyList<string> Split(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (character is not ('\r' or '\n'))
            {
                continue;
            }

            lines.Add(text[start..i]);
            var isCrLf = character == '\r' && i + 1 < text.Length && text[i + 1] == '\n';
            if (isCrLf)
            {
                i++;
            }

            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }
}
