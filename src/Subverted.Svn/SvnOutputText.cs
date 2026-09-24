using System.Text;
using System.Text.Unicode;

namespace Subverted.Svn;

/// <summary>
/// Turns what svn wrote into text. Its own lines — paths, notifications, errors — are in the
/// encoding it was built to write, while a diff's content lines are the file's own bytes, so each
/// line is decoded on its own: as UTF-8 when it is valid UTF-8, and in the other encoding when not.
/// </summary>
/// <remarks>
/// A line in a legacy code page that happens to be valid UTF-8 would be misread; for real names
/// that takes a sequence like <c>Ĺ‚</c> in CP1250, which is not a thing anyone types.
/// </remarks>
public static class SvnOutputText
{
    private static readonly UTF8Encoding Utf8Text = new(encoderShouldEmitUTF8Identifier: false);

    public static string Decode(ReadOnlySpan<byte> output, Encoding linesThatAreNotUtf8)
    {
        var text = new StringBuilder(output.Length);
        while (!output.IsEmpty)
        {
            var end = output.IndexOf((byte)'\n');
            var line = end < 0 ? output : output[..(end + 1)];

            var encoding = Utf8.IsValid(line) ? Utf8Text : linesThatAreNotUtf8;
            text.Append(encoding.GetString(line));
            output = output[line.Length..];
        }

        return text.ToString();
    }
}
