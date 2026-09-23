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
