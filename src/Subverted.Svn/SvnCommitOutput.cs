using System.Globalization;

namespace Subverted.Svn;

/// <summary>
/// The revision number out of what <c>svn commit</c> printed. Pure, and parsing English on purpose:
/// <see cref="SvnCommand"/> forces the C locale, so this line reads the same on every machine in
/// the studio whatever language SVN was installed in.
/// </summary>
public static class SvnCommitOutput
{
    private const string Prefix = "Committed revision ";

    /// <returns>
    /// The revision, or <see langword="null"/> when SVN did not report one — which is what a commit
    /// with nothing to send looks like, and is success rather than failure.
    /// </returns>
    public static long? Revision(string standardOutput)
    {
        // Last, not first: a commit that triggers a post-commit hook failure still reports its
        // revision, and anything SVN prints afterwards must not shadow it.
        long? found = null;
        foreach (var line in standardOutput.Split('\n'))
        {
            if (RevisionIn(line.Trim()) is { } revision)
            {
                found = revision;
            }
        }

        return found;
    }

    private static long? RevisionIn(string line)
    {
        if (!line.StartsWith(Prefix, StringComparison.Ordinal) || !line.EndsWith('.'))
        {
            return null;
        }

        var digits = line[Prefix.Length..^1];
        return long.TryParse(
            digits,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed
            : null;
    }
}
