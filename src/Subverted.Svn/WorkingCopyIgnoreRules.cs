namespace Subverted.Svn;

/// <summary>
/// Which unversioned names a working copy ignores, with each directory's inherited patterns
/// already accumulated so a lookup does not walk back up the tree.
/// </summary>
internal sealed class WorkingCopyIgnoreRules
{
    private readonly Dictionary<string, Rules> _byDirectory;
    private readonly IReadOnlyList<string> _globalPatterns;

    private WorkingCopyIgnoreRules(
        Dictionary<string, Rules> byDirectory,
        IReadOnlyList<string> globalPatterns
    )
    {
        _byDirectory = byDirectory;
        _globalPatterns = globalPatterns;
    }

    /// <param name="declared">
    /// Every versioned directory, keyed by relative path with the root as the empty string.
    /// Directories that declare nothing must still be present, or the inheritance chain breaks.
    /// </param>
    /// <param name="globalPatterns">Runtime-config patterns, in force everywhere.</param>
    public static WorkingCopyIgnoreRules Build(
        IReadOnlyDictionary<string, DirectoryIgnorePatterns> declared,
        IReadOnlyList<string> globalPatterns
    )
    {
        var byDirectory = new Dictionary<string, Rules>(declared.Count, StringComparer.Ordinal);

        // Ordinal order puts a directory before everything beneath it, so a parent's accumulated
        // patterns are always already in the map.
        foreach (var relPath in declared.Keys.Order(StringComparer.Ordinal))
        {
            var patterns = declared[relPath];
            var inherited = InheritedFor(byDirectory, relPath, globalPatterns);

            byDirectory[relPath] = new Rules(
                patterns.ImmediateChildren,
                patterns.Descendants.Count == 0
                    ? inherited
                    : [.. inherited, .. patterns.Descendants]
            );
        }

        return new WorkingCopyIgnoreRules(byDirectory, globalPatterns);
    }

    /// <param name="directoryRelPath">The containing directory; the empty string for the root.</param>
    /// <param name="name">A single path segment.</param>
    public bool IsIgnored(string directoryRelPath, string name) =>
        _byDirectory.TryGetValue(directoryRelPath, out var rules)
            ? MatchesAny(rules.ImmediateChildren, name) || MatchesAny(rules.Inherited, name)
            : MatchesAny(_globalPatterns, name);

    private static IReadOnlyList<string> InheritedFor(
        Dictionary<string, Rules> byDirectory,
        string relPath,
        IReadOnlyList<string> globalPatterns
    )
    {
        var separator = relPath.LastIndexOf('/');
        if (separator < 0)
        {
            return relPath.Length == 0 || !byDirectory.TryGetValue(string.Empty, out var root)
                ? globalPatterns
                : root.Inherited;
        }

        return byDirectory.TryGetValue(relPath[..separator], out var parent)
            ? parent.Inherited
            : globalPatterns;
    }

    private static bool MatchesAny(IReadOnlyList<string> patterns, string name)
    {
        foreach (var pattern in patterns)
        {
            if (SvnGlob.Matches(pattern, name))
            {
                return true;
            }
        }

        return false;
    }

    /// <param name="Inherited">Runtime-config patterns plus every ancestor's, this one included.</param>
    private readonly record struct Rules(
        IReadOnlyList<string> ImmediateChildren,
        IReadOnlyList<string> Inherited
    );
}
