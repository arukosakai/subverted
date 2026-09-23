using System.Globalization;
using System.Text.RegularExpressions;
using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Reads what <c>svnversion</c> printed: <c>9</c>, <c>3:5</c>, either with any of the <c>M</c>,
/// <c>S</c> and <c>P</c> flags after it, or a sentence for a path with no BASE at all.
/// </summary>
public static partial class SvnVersionOutput
{
    /// <summary>The sentences 1.8.15 prints for a path that has no BASE revision.</summary>
    private static readonly string[] NoBaseSentences =
    [
        "Uncommitted local addition, copy or move",
        "Unversioned file",
        "Unversioned directory",
        "Unversioned symlink",
    ];

    /// <returns>The range, or null for a path SVN says has no BASE.</returns>
    /// <exception cref="SvnCommandException">Something else was printed.</exception>
    public static BaseRevisionRange? Parse(string output)
    {
        var printed = output.Trim();
        if (NoBaseSentences.Contains(printed, StringComparer.Ordinal))
        {
            return null;
        }

        var match = Range().Match(printed);
        if (!match.Success)
        {
            throw new SvnCommandException($"svnversion printed '{printed}', which is not a range.");
        }

        var lowest = long.Parse(match.Groups["lowest"].Value, CultureInfo.InvariantCulture);
        var highest = match.Groups["highest"].Success
            ? long.Parse(match.Groups["highest"].Value, CultureInfo.InvariantCulture)
            : lowest;
        return new BaseRevisionRange(lowest, highest);
    }

    [GeneratedRegex("^(?<lowest>[0-9]+)(?::(?<highest>[0-9]+))?[MSP]*$")]
    private static partial Regex Range();
}
