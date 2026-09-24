namespace Subverted.Svn;

/// <summary>
/// Puts the true names back into the headers of a diff svn wrote in a code page that could not hold
/// them. Only header positions are touched: <c>Index:</c>, <c>Property changes on:</c>, and the
/// <c>---</c>/<c>+++</c> pair right after an Index separator — a content line can look like either.
/// </summary>
public static class DiffHeaderRespelling
{
    private const string IndexPrefix = "Index: ";
    private const string PropertiesPrefix = "Property changes on: ";
    private const int HeaderPairPrefixLength = 4; // "--- " and "+++ "
    private const string IndexSeparator =
        "===================================================================";

    /// <param name="summarisedPaths">
    /// The true paths, slash-separated. A header may name any trailing part of one — a revision diff
    /// names files relative to its target — so every such tail is a candidate.
    /// </param>
    /// <param name="asSvnWouldPrint">See <see cref="ISvnTextSpelling.AsSvnWouldPrint"/>.</param>
    /// <returns>
    /// The diff with each header svn mangled respelled, where exactly one true name mangles that way.
    /// A header two names share is left as svn wrote it rather than guessed.
    /// </returns>
    public static string Respell(
        string diff,
        IEnumerable<string> summarisedPaths,
        Func<string, string> asSvnWouldPrint
    )
    {
        var trueNames = TrueNamesBySpelling(summarisedPaths, asSvnWouldPrint);
        if (trueNames.Count == 0)
        {
            return diff;
        }

        var lines = diff.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (IsHeaderPairLine(lines, index))
            {
                lines[index] = Respell(lines[index], HeaderPairPrefixLength, trueNames);
            }
            else if (lines[index].StartsWith(IndexPrefix, StringComparison.Ordinal))
            {
                lines[index] = Respell(lines[index], IndexPrefix.Length, trueNames);
            }
            else if (lines[index].StartsWith(PropertiesPrefix, StringComparison.Ordinal))
            {
                lines[index] = Respell(lines[index], PropertiesPrefix.Length, trueNames);
            }
        }

        return string.Join('\n', lines);
    }

    private static Dictionary<string, string> TrueNamesBySpelling(
        IEnumerable<string> summarisedPaths,
        Func<string, string> asSvnWouldPrint
    ) =>
        summarisedPaths
            .SelectMany(Tails)
            .Distinct(StringComparer.Ordinal)
            .Select(name => (Name: name, Spelled: asSvnWouldPrint(name)))
            .Where(candidate => candidate.Spelled != candidate.Name)
            .GroupBy(candidate => candidate.Spelled, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Name, StringComparer.Ordinal);

    private static IEnumerable<string> Tails(string path)
    {
        yield return path;
        for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
        {
            yield return path[(slash + 1)..];
        }
    }

    /// <summary>The <c>---</c> line two after an Index line, or the <c>+++</c> line after that.</summary>
    private static bool IsHeaderPairLine(string[] lines, int index)
    {
        bool FollowsIndex(int separator) =>
            separator >= 1
            && Unterminated(lines[separator]) == IndexSeparator
            && lines[separator - 1].StartsWith(IndexPrefix, StringComparison.Ordinal);

        var line = lines[index];
        return line.StartsWith("--- ", StringComparison.Ordinal) && FollowsIndex(index - 1)
            || line.StartsWith("+++ ", StringComparison.Ordinal)
                && index >= 1
                && lines[index - 1].StartsWith("--- ", StringComparison.Ordinal)
                && FollowsIndex(index - 2);
    }

    private static string Respell(
        string line,
        int prefixLength,
        Dictionary<string, string> trueNames
    )
    {
        // svn follows the name on a ---/+++ line with a tab and the revision; the other headers end.
        var name = Unterminated(line[prefixLength..]).Split('\t')[0];
        return trueNames.TryGetValue(name, out var trueName)
            ? line[..prefixLength] + trueName + line[(prefixLength + name.Length)..]
            : line;
    }

    private static string Unterminated(string line) => line.TrimEnd('\r');
}
