using System.Globalization;

namespace Subverted.Svn;

/// <summary>
/// How a path is written on an <c>svn</c> command line. Pure, because getting it wrong is not a
/// crash — it is a diff whose every header reads
/// <c>C:/Users/.../Temp/subverted-it-a311.../wc/src/a.txt</c> instead of <c>src/a.txt</c>.
/// </summary>
public static class SvnTarget
{
    /// <summary>
    /// The path as a client running in <paramref name="workingCopyRoot"/> should be given it:
    /// relative, <c>.</c> for the root itself, and safe to hand to any subcommand that reads a peg
    /// revision off its targets — which is every one Subverted runs except <c>svn diff</c>.
    /// </summary>
    /// <remarks>
    /// The separator is left alone deliberately. It makes no difference to what SVN prints — it
    /// re-spells every path in the platform's own separator whichever one it was given, which is
    /// <see cref="SvnNotification"/>'s problem — and rewriting it here would corrupt a filename
    /// that legitimately contains a backslash on a Unix checkout.
    /// </remarks>
    public static string Within(string workingCopyRoot, string path) =>
        PegSafe(Relative(workingCopyRoot, path));

    /// <summary>
    /// The same target for <c>svn diff</c>, the one subcommand that reads the whole argument as a
    /// path. Measured on 1.8.15: <c>svn diff icon@2x.png</c> diffs the file, and the terminator
    /// <see cref="Within"/> appends is taken as part of the filename and reported unversioned.
    /// </summary>
    public static string WithinForDiff(string workingCopyRoot, string path) =>
        Relative(workingCopyRoot, path);

    /// <summary>
    /// The same target for <c>svnversion</c>, which like <c>svn diff</c> takes the argument whole:
    /// measured on 1.8.15, <c>svnversion icon@2x.png</c> answers and <c>icon@2x.png@</c> does not exist.
    /// </summary>
    public static string WithinForSvnversion(string workingCopyRoot, string path) =>
        Relative(workingCopyRoot, path);

    /// <summary>
    /// A path as it stood in the repository at <paramref name="revision"/>: a URL pegged there, so
    /// it names the node that revision touched even after it was deleted or renamed.
    /// </summary>
    /// <param name="repositoryRoot">The root URL, as wc.db records it — already escaped.</param>
    /// <param name="repositoryPath">Repository-absolute, unescaped, as <c>svn log</c> prints it.</param>
    /// <remarks>
    /// Each segment is escaped: a name holding <c>%41</c> would otherwise be read as <c>A</c>. The
    /// peg is always written, and an escaped <c>@</c> in a name cannot be mistaken for it.
    /// </remarks>
    public static string InRepository(string repositoryRoot, string repositoryPath, long revision)
    {
        var escaped = string.Join(
            '/',
            repositoryPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString)
        );
        var url =
            escaped.Length == 0
                ? repositoryRoot.TrimEnd('/')
                : $"{repositoryRoot.TrimEnd('/')}/{escaped}";

        return $"{url}@{revision.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string Relative(string workingCopyRoot, string path) =>
        Path.GetRelativePath(workingCopyRoot, path);

    /// <remarks>
    /// <c>svn</c> splits a target at its last <c>@</c> and reads what follows as a revision, so
    /// <c>icon@2x.png</c> — the ordinary name for a retina asset — fails the command outright. A
    /// trailing <c>@</c> is SVN's own escape and is stripped before the path is used. It is
    /// appended only where it is needed, because <c>.@</c> is itself an error.
    /// </remarks>
    private static string PegSafe(string target) =>
        target.Contains('@', StringComparison.Ordinal) ? target + '@' : target;
}
