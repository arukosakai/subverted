using System.Globalization;

namespace Subverted.Svn;

/// <summary>
/// Reads what <c>svn update</c> printed. Pure, and parsing English on purpose:
/// <see cref="SvnCommand"/> forces the C locale, so these lines read the same on every machine in
/// the studio whatever language SVN was installed in.
/// </summary>
/// <remarks>
/// This exists because <c>svn update</c> exits zero on a conflict. Its exit code says the client
/// ran, not that the merge succeeded, so the only place the difference is stated is this text.
/// </remarks>
public static class SvnUpdateOutput
{
    private const string UpdatedPrefix = "Updated to revision ";
    private const string UnchangedPrefix = "At revision ";

    private static readonly string[] ConflictLabels =
    [
        "Text conflicts:",
        "Property conflicts:",
        "Tree conflicts:",
    ];

    private static readonly string[] SkippedLabels = ["Skipped paths:"];

    /// <returns>
    /// The revision the working copy now stands at, or <see langword="null"/> when SVN reported
    /// none — which is what a failed update looks like, and never a successful one.
    /// </returns>
    public static long? Revision(string standardOutput)
    {
        // Last, not first: a working copy with an external gets one of these lines per checkout,
        // and the one that answers "what revision am I at" is the target's own, which comes last.
        long? found = null;
        foreach (var line in Lines(standardOutput))
        {
            if (RevisionIn(line) is { } revision)
            {
                found = revision;
            }
        }

        return found;
    }

    /// <returns>Text, property and tree conflicts added together, as SVN counted them.</returns>
    public static int Conflicts(string standardOutput) => Counted(standardOutput, ConflictLabels);

    /// <returns>How many paths SVN declined to touch.</returns>
    public static int SkippedPaths(string standardOutput) => Counted(standardOutput, SkippedLabels);

    /// <remarks>
    /// The trailing full stop is checked before the digits are taken, because the slice that drops
    /// it would otherwise turn an unterminated "Updated to revision 12" into revision 1.
    /// </remarks>
    private static long? RevisionIn(string line) =>
        !line.EndsWith('.')
            ? null
            : RevisionAfter(line, UpdatedPrefix) ?? RevisionAfter(line, UnchangedPrefix);

    private static long? RevisionAfter(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal)
        && long.TryParse(
            line[prefix.Length..^1],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed
            : null;

    /// <summary>
    /// Adds up the counts SVN printed under its summary heading.
    /// </summary>
    /// <remarks>
    /// The heading itself is not looked for. Every other line an update prints begins with a status
    /// column or a quoted path, so a line that starts with one of these labels came from the
    /// summary block and nowhere else — and a file named <c>Text conflicts: 9</c> is announced as
    /// <c>A    Text conflicts: 9</c>, which does not.
    /// </remarks>
    private static int Counted(string standardOutput, IReadOnlyList<string> labels)
    {
        var total = 0;
        foreach (var line in Lines(standardOutput))
        {
            foreach (var label in labels)
            {
                if (CountAfter(line, label) is { } counted)
                {
                    total += counted;
                }
            }
        }

        return total;
    }

    private static int? CountAfter(string line, string label) =>
        line.StartsWith(label, StringComparison.Ordinal)
        && int.TryParse(
            line[label.Length..].Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed
            : null;

    private static IEnumerable<string> Lines(string standardOutput) =>
        standardOutput.Split('\n').Select(line => line.Trim());
}
