namespace Subverted.Svn;

/// <summary>
/// The ignore globs one versioned directory declares, split by how far they reach.
/// </summary>
/// <param name="ImmediateChildren"><c>svn:ignore</c> — this directory's own entries, not deeper.</param>
/// <param name="Descendants"><c>svn:global-ignores</c> — this directory and everything under it.</param>
internal sealed record DirectoryIgnorePatterns(
    IReadOnlyList<string> ImmediateChildren,
    IReadOnlyList<string> Descendants
)
{
    public static readonly DirectoryIgnorePatterns None = new([], []);

    /// <summary>
    /// Both properties are newline-delimited. That is worth stating because the documentation
    /// describes <c>svn:global-ignores</c> as whitespace-delimited like its config-file namesake,
    /// and on SVN 1.8.15 it is not: a space-separated value matched only a file with a space in
    /// its name.
    /// </summary>
    /// <param name="properties">Working property blob; an unparseable one declares nothing.</param>
    public static DirectoryIgnorePatterns FromProperties(byte[]? properties)
    {
        if (SvnPropertySkel.Parse(properties) is not { } parsed)
        {
            return None;
        }

        IReadOnlyList<string> immediateChildren = [];
        IReadOnlyList<string> descendants = [];

        foreach (var property in parsed)
        {
            switch (property.Name)
            {
                case "svn:ignore":
                    immediateChildren = SplitLines(property.Value);
                    break;
                case "svn:global-ignores":
                    descendants = SplitLines(property.Value);
                    break;
            }
        }

        return immediateChildren.Count == 0 && descendants.Count == 0
            ? None
            : new DirectoryIgnorePatterns(immediateChildren, descendants);
    }

    private static string[] SplitLines(string value) =>
        value.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
}
