namespace Subverted.Core.Tests;

/// <summary>
/// The one place that decides whether a node the daemon reported is under a path the user typed.
/// Getting it wrong either hides a change from a listing or claims a sibling that merely shares a
/// prefix, and both are quiet.
/// </summary>
public sealed class TargetCoverageTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;
    private const StringComparison Insensitive = StringComparison.OrdinalIgnoreCase;

    private static readonly string Root = Path.GetFullPath("/wc");

    [Test]
    public async Task A_path_under_the_root_becomes_the_slash_separated_relative_one()
    {
        var relative = TargetCoverage.RelativeTo(Root, Path.Combine(Root, "art", "hero.png"));

        await Assert.That(relative).IsEqualTo("art/hero.png");
    }

    /// <summary>The root itself is what entries spell as the empty string, not as <c>.</c>.</summary>
    [Test]
    public async Task The_root_itself_becomes_the_empty_relative_path()
    {
        await Assert.That(TargetCoverage.RelativeTo(Root, Root)).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("art", "art", true)]
    [Arguments("art", "art/hero.png", true)]
    [Arguments("art", "artefacts/notes.txt", false)]
    [Arguments("art", "src/a.txt", false)]
    [Arguments("", "art/hero.png", true)]
    [Arguments("", "", true)]
    public async Task Covers_is_the_target_itself_and_everything_beneath_it(
        string target,
        string relPath,
        bool expected
    )
    {
        await Assert.That(TargetCoverage.Covers(target, relPath, Sensitive)).IsEqualTo(expected);
    }

    /// <summary>
    /// The difference between the two: <c>Below</c> excludes the ancestor itself, which is what
    /// keeps a directory from being treated as settled by its own answer.
    /// </summary>
    [Test]
    [Arguments("art", "art", false)]
    [Arguments("art", "art/hero.png", true)]
    [Arguments("art", "artefacts/notes.txt", false)]
    [Arguments("", "art/hero.png", true)]
    [Arguments("", "", false)]
    public async Task Below_excludes_the_ancestor_itself(
        string ancestor,
        string relPath,
        bool expected
    )
    {
        await Assert.That(TargetCoverage.Below(ancestor, relPath, Sensitive)).IsEqualTo(expected);
    }

    /// <summary>
    /// Windows compares paths without case and everything else compares them with it. Both answers
    /// are covered here so neither platform's behaviour rests on which machine ran the suite.
    /// </summary>
    [Test]
    public async Task Case_is_the_comparison_it_was_given_and_not_the_machines()
    {
        await Assert.That(TargetCoverage.Covers("art", "ART/hero.png", Sensitive)).IsFalse();
        await Assert.That(TargetCoverage.Covers("art", "ART/hero.png", Insensitive)).IsTrue();
        await Assert.That(TargetCoverage.Below("art", "ART/hero.png", Sensitive)).IsFalse();
        await Assert.That(TargetCoverage.Below("art", "ART/hero.png", Insensitive)).IsTrue();
    }
}
