namespace Subverted.Svn;

/// <summary>
/// Pulls the <c>svn: warning:</c> lines out of a client's stderr. Pure, and reading English on
/// purpose: <see cref="SvnCommand"/> forces the C locale, so these lines read the same on every
/// machine in the studio whatever language SVN was installed in.
/// </summary>
/// <remarks>
/// A warning is how SVN reports a path it would not do, and what that costs a caller differs by
/// command — <c>lock</c> and <c>unlock</c> exit <em>zero</em> after refusing every path they were
/// given, while <c>resolve</c> exits one. Either way the exit code alone cannot say which paths,
/// so each command documents what a warning of its own means and they all read them the same way.
/// </remarks>
public static class SvnWarnings
{
    private const string WarningPrefix = "svn: warning:";

    /// <returns>
    /// One entry per warning line, in SVN's own words. Its wording names the path and, for a lock
    /// somebody else holds, the user holding it, which is more than a re-worded version would
    /// carry.
    /// </returns>
    /// <remarks>
    /// One warning is taken to be one line. That is what SVN printed for every refusal these
    /// commands can produce here; a warning that wrapped would be counted twice, which over-reports
    /// rather than hides one.
    /// </remarks>
    public static IReadOnlyList<string> From(string standardError) =>
        [
            .. standardError
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(WarningPrefix, StringComparison.Ordinal)),
        ];
}
