namespace Subverted.Svn;

/// <summary>
/// Reads the nodes <c>svn resolve</c> says it resolved out of its own notification text. Pure, and
/// reading English on purpose — <see cref="SvnCommand"/> forces the C locale.
/// </summary>
/// <remarks>
/// This exists because <c>svn resolve</c> is silent about a path it had nothing to do for and still
/// exits zero, so the count of these lines is the only thing separating "resolved it" from "looked
/// at it and walked away". The wording is identical for a text, property and tree conflict, which
/// is why one parser covers all three.
/// </remarks>
public static class SvnResolveOutput
{
    private const string Prefix = "Resolved conflicted state of '";

    /// <param name="backslashIsOnlyASeparator">
    /// Whether this platform reserves <c>\</c>. SVN echoes the platform separator and every
    /// <c>RelPath</c> Subverted reports is slash-separated; taken as an argument for the reason
    /// given on <see cref="SvnNotification"/>.
    /// </param>
    /// <returns>
    /// One path per node resolved, in the order SVN printed them — its own tree order, not the
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
                .Select(path => backslashIsOnlyASeparator ? path.Replace('\\', '/') : path),
        ];

    /// <returns>The path inside the quotes, or null when the line is not one of these.</returns>
    private static string? Quoted(string line)
    {
        if (!line.StartsWith(Prefix, StringComparison.Ordinal) || !line.EndsWith('\''))
        {
            return null;
        }

        var path = line[Prefix.Length..^1];
        return path.Length == 0 ? null : path;
    }
}
