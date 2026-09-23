namespace Subverted.Svn;

/// <summary>
/// The <c>global-ignores</c> setting from Subversion's runtime configuration — the patterns in
/// force in every directory of every working copy on this machine.
/// </summary>
public static class GlobalIgnoreConfiguration
{
    /// <summary>
    /// What Subversion 1.8 ignores when the runtime config leaves the setting commented out.
    /// Taken from a stock config file and confirmed one pattern at a time against
    /// <c>svn status</c>: <c>Thumbs.db</c> is deliberately absent, because it is not ignored.
    /// </summary>
    public static readonly IReadOnlyList<string> SubversionDefault =
    [
        "*.o",
        "*.lo",
        "*.la",
        "*.al",
        ".libs",
        "*.so",
        "*.so.[0-9]*",
        "*.a",
        "*.pyc",
        "*.pyo",
        "__pycache__",
        "*.rej",
        "*~",
        "#*#",
        ".#*",
        ".*.swp",
        ".DS_Store",
    ];

    /// <summary>
    /// Reads the config file, falling back to <see cref="SubversionDefault"/> when it is missing
    /// or unreadable. Subversion's Windows registry overrides are not consulted, so a machine
    /// configured that way will disagree with <c>svn status</c>.
    /// </summary>
    public static IReadOnlyList<string> Load()
    {
        try
        {
            var path = ConfigFilePath();
            return File.Exists(path) ? Parse(File.ReadLines(path)) : SubversionDefault;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SubversionDefault;
        }
    }

    /// <summary>
    /// Reads <c>global-ignores</c> out of the <c>[miscellany]</c> section. The value is
    /// whitespace-delimited — unlike the <c>svn:ignore</c> and <c>svn:global-ignores</c>
    /// properties, which are newline-delimited — and continues onto any following indented line.
    /// </summary>
    /// <returns>
    /// The configured patterns, which <em>replace</em> rather than extend
    /// <see cref="SubversionDefault"/>; that default when the setting is absent.
    /// </returns>
    public static IReadOnlyList<string> Parse(IEnumerable<string> lines)
    {
        var patterns = new List<string>();
        var found = false;
        var inMiscellany = false;
        var continuing = false;

        foreach (var line in lines)
        {
            var content = line.Trim();
            if (content.Length == 0 || content[0] is '#' or ';')
            {
                continuing = false;
                continue;
            }

            if (char.IsWhiteSpace(line[0]))
            {
                if (continuing)
                {
                    patterns.AddRange(SplitOnWhitespace(content));
                }

                continue;
            }

            continuing = false;

            if (content[0] == '[')
            {
                inMiscellany = content.Equals("[miscellany]", StringComparison.Ordinal);
                continue;
            }

            var separator = content.IndexOf('=');
            if (separator < 0 || !inMiscellany)
            {
                continue;
            }

            if (!content[..separator].TrimEnd().Equals("global-ignores", StringComparison.Ordinal))
            {
                continue;
            }

            found = true;
            continuing = true;
            patterns.AddRange(SplitOnWhitespace(content[(separator + 1)..]));
        }

        // An explicitly empty setting means "ignore nothing", which is not the same as unset.
        return found ? patterns : SubversionDefault;
    }

    private static string[] SplitOnWhitespace(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Subversion looks in <c>%APPDATA%</c> on Windows and <c>~/.subversion</c> elsewhere.</summary>
    private static string ConfigFilePath() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Subversion",
                "config"
            )
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".subversion",
                "config"
            );
}
