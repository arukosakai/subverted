using System.Globalization;

namespace Subverted.Frontend.Diff;

/// <summary>The line ranges a hunk header announces, which is also how many lines its body holds.</summary>
internal sealed record HunkRange(int OldStart, int OldCount, int NewStart, int NewCount)
{
    public const string ContentFence = "@@";
    public const string PropertyFence = "##";

    /// <summary>
    /// Reads <c>@@ -a,b +c,d @@</c> — or <c>## … ##</c> for a property — where an omitted count
    /// means one line, and anything after the closing fence is ignored.
    /// </summary>
    /// <returns>The ranges, or <c>null</c> when the line is not a header of that fence.</returns>
    public static HunkRange? Parse(string line, string fence)
    {
        var opening = fence + " -";
        if (!line.StartsWith(opening, StringComparison.Ordinal))
        {
            return null;
        }

        var closing = line.IndexOf(" " + fence, opening.Length, StringComparison.Ordinal);
        if (closing < 0)
        {
            return null;
        }

        var ranges = line[opening.Length..closing].Split(" +");
        if (ranges.Length != 2)
        {
            return null;
        }

        return ParseSide(ranges[0]) is { } old && ParseSide(ranges[1]) is { } @new
            ? new HunkRange(old.Start, old.Count, @new.Start, @new.Count)
            : null;
    }

    private static (int Start, int Count)? ParseSide(string side)
    {
        var comma = side.IndexOf(',');
        var start = comma < 0 ? side : side[..comma];
        var count = comma < 0 ? "1" : side[(comma + 1)..];

        return IsLineNumber(start, out var startValue) && IsLineNumber(count, out var countValue)
            ? (startValue, countValue)
            : null;
    }

    private static bool IsLineNumber(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
