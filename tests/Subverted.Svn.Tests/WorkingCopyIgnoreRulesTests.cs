namespace Subverted.Svn.Tests;

/// <summary>
/// The reach of each property is the whole point of this type, and the fixture settled it:
/// a root `svn:ignore` of `*.tmp` left `sub/child.tmp` reported, while a root
/// `svn:global-ignores` reached two levels down.
/// </summary>
public sealed class WorkingCopyIgnoreRulesTests
{
    [Test]
    [Arguments("", "root.tmp", true)]
    [Arguments("sub", "child.tmp", false)]
    [Arguments("sub/deep", "deep.tmp", false)]
    public async Task Svn_ignore_reaches_immediate_children_only(
        string directory,
        string name,
        bool expected
    )
    {
        var rules = Build(
            globalPatterns: [],
            ("", Immediate("*.tmp")),
            ("sub", DirectoryIgnorePatterns.None),
            ("sub/deep", DirectoryIgnorePatterns.None)
        );

        await Assert.That(rules.IsIgnored(directory, name)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("", true)]
    [Arguments("sub", true)]
    [Arguments("sub/deep", true)]
    public async Task Svn_global_ignores_reaches_every_descendant(string directory, bool expected)
    {
        var rules = Build(
            globalPatterns: [],
            ("", Descendant("deep.xyz")),
            ("sub", DirectoryIgnorePatterns.None),
            ("sub/deep", DirectoryIgnorePatterns.None)
        );

        await Assert.That(rules.IsIgnored(directory, "deep.xyz")).IsEqualTo(expected);
    }

    /// <summary>
    /// A directory adding its own inheritable patterns must not drop the ones handed down to it.
    /// </summary>
    [Test]
    [Arguments("", "fromroot.xyz", true)]
    [Arguments("", "frommid.xyz", false)]
    [Arguments("mid", "fromroot.xyz", true)]
    [Arguments("mid", "frommid.xyz", true)]
    [Arguments("mid/leaf", "fromroot.xyz", true)]
    [Arguments("mid/leaf", "frommid.xyz", true)]
    public async Task Inheritable_patterns_accumulate_down_the_tree(
        string directory,
        string name,
        bool expected
    )
    {
        var rules = Build(
            globalPatterns: [],
            ("", Descendant("fromroot.xyz")),
            ("mid", Descendant("frommid.xyz")),
            ("mid/leaf", DirectoryIgnorePatterns.None)
        );

        await Assert.That(rules.IsIgnored(directory, name)).IsEqualTo(expected);
    }

    /// <summary>
    /// A sibling's inheritable patterns must not leak sideways, which a flat accumulation would
    /// let happen.
    /// </summary>
    [Test]
    [Arguments("left", true)]
    [Arguments("right", false)]
    public async Task Inheritable_patterns_do_not_reach_a_sibling(string directory, bool expected)
    {
        var rules = Build(
            globalPatterns: [],
            ("", DirectoryIgnorePatterns.None),
            ("left", Descendant("only-left.xyz")),
            ("right", DirectoryIgnorePatterns.None)
        );

        await Assert.That(rules.IsIgnored(directory, "only-left.xyz")).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("sub")]
    [Arguments("sub/deep")]
    public async Task Runtime_config_patterns_apply_in_every_directory(string directory)
    {
        var rules = Build(
            globalPatterns: ["*.o"],
            ("", DirectoryIgnorePatterns.None),
            ("sub", DirectoryIgnorePatterns.None),
            ("sub/deep", DirectoryIgnorePatterns.None)
        );

        await Assert.That(rules.IsIgnored(directory, "t.o")).IsTrue();
    }

    /// <summary>
    /// An unversioned directory has no wc.db row and so declares nothing, but the machine-wide
    /// patterns still hold inside it.
    /// </summary>
    [Test]
    [Arguments("t.o", true)]
    [Arguments("t.tmp", false)]
    public async Task A_directory_with_no_row_still_gets_the_runtime_config_patterns(
        string name,
        bool expected
    )
    {
        var rules = Build(globalPatterns: ["*.o"], ("", Immediate("*.tmp")));

        await Assert.That(rules.IsIgnored("never-versioned", name)).IsEqualTo(expected);
    }

    /// <summary>
    /// Building without a root entry should not throw and should not silently ignore everything;
    /// the machine-wide patterns are the only ones that can still be trusted.
    /// </summary>
    [Test]
    [Arguments("t.o", true)]
    [Arguments("t.tmp", true)]
    [Arguments("plain.txt", false)]
    public async Task A_missing_root_entry_falls_back_to_the_runtime_config_patterns(
        string name,
        bool expected
    )
    {
        var rules = Build(globalPatterns: ["*.o"], ("orphan", Immediate("*.tmp")));

        await Assert.That(rules.IsIgnored("orphan", name)).IsEqualTo(expected);
    }

    /// <summary>
    /// The same fallback one level down: a nested directory whose parent is missing from the map
    /// cannot inherit from it, and must not lose the machine-wide patterns as well.
    /// </summary>
    [Test]
    [Arguments("t.o", true)]
    [Arguments("plain.txt", false)]
    public async Task A_missing_parent_entry_falls_back_to_the_runtime_config_patterns(
        string name,
        bool expected
    )
    {
        var rules = Build(globalPatterns: ["*.o"], ("gap/below", DirectoryIgnorePatterns.None));

        await Assert.That(rules.IsIgnored("gap/below", name)).IsEqualTo(expected);
    }

    [Test]
    public async Task A_name_matching_nothing_is_not_ignored()
    {
        var rules = Build(globalPatterns: ["*.o"], ("", Immediate("*.tmp")));

        await Assert.That(rules.IsIgnored("", "plain.txt")).IsFalse();
    }

    private static WorkingCopyIgnoreRules Build(
        IReadOnlyList<string> globalPatterns,
        params (string RelPath, DirectoryIgnorePatterns Patterns)[] directories
    ) =>
        WorkingCopyIgnoreRules.Build(
            directories.ToDictionary(d => d.RelPath, d => d.Patterns, StringComparer.Ordinal),
            globalPatterns
        );

    private static DirectoryIgnorePatterns Immediate(params string[] patterns) => new(patterns, []);

    private static DirectoryIgnorePatterns Descendant(params string[] patterns) =>
        new([], patterns);
}
