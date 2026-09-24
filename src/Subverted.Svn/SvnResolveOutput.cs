namespace Subverted.Svn;

/// <summary>
/// Reads the nodes <c>svn resolve</c> says it resolved out of its own notification text. Pure, and
/// reading English on purpose — <see cref="SvnCommand"/> forces an English locale.
/// </summary>
/// <remarks>
/// This exists because <c>svn resolve</c> is silent about a path it had nothing to do for and still
/// exits zero, so the count of these lines is the only thing separating "resolved it" from "looked
/// at it and walked away". 1.8 words every kind of conflict the same way; 1.14 has a line of its own
/// for text and tree conflicts, and announces a node once per kind it had.
/// </remarks>
public static class SvnResolveOutput
{
    private static readonly Announcement[] Announcements =
    [
        new("Resolved conflicted state of '", "'"),
        new("Merge conflicts in '", "' marked as resolved."),
        new("Tree conflict at '", "' marked as resolved."),
    ];

    /// <param name="backslashIsOnlyASeparator">
    /// Whether this platform reserves <c>\</c>. SVN echoes the platform separator and every
    /// <c>RelPath</c> Subverted reports is slash-separated; taken as an argument for the reason
    /// given on <see cref="SvnNotification"/>.
    /// </param>
    /// <returns>
    /// One path per node resolved, in the order SVN first named them — its own tree order, not the
    /// order the targets were given. Empty when nothing was conflicted.
    /// </returns>
    public static IReadOnlyList<string> ResolvedPaths(
        string standardOutput,
        bool backslashIsOnlyASeparator
    ) =>
        [
            .. standardOutput
                .Split('\n')
                .Select(line => line.Trim())
                .Select(Quoted)
                .OfType<string>()
                .Select(path => backslashIsOnlyASeparator ? path.Replace('\\', '/') : path)
                .Distinct(StringComparer.Ordinal),
        ];

    /// <returns>The path inside the quotes, or null when the line is not one of these.</returns>
    private static string? Quoted(string line) =>
        Announcements
            .Select(announcement => announcement.PathIn(line))
            .FirstOrDefault(path => path is not null);

    private sealed record Announcement(string Prefix, string Suffix)
    {
        public string? PathIn(string line)
        {
            var isThisShape =
                line.Length > Prefix.Length + Suffix.Length
                && line.StartsWith(Prefix, StringComparison.Ordinal)
                && line.EndsWith(Suffix, StringComparison.Ordinal);

            return isThisShape ? line[Prefix.Length..^Suffix.Length] : null;
        }
    }
}
