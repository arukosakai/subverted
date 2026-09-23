namespace Subverted.Daemon.Tests;

/// <summary>
/// Prefix matching on paths is where "it worked on my machine" lives: <c>/wc</c> claiming
/// <c>/wc2</c>, a nested checkout answering as its parent, a drive letter in the wrong case.
/// </summary>
public sealed class ContainingRootTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    [Test]
    public async Task A_root_contains_itself()
    {
        await Assert.That(ContainingRoot.Of("/wc", ["/wc"], Sensitive)).IsEqualTo("/wc");
    }

    [Test]
    [Arguments("/wc/art")]
    [Arguments("/wc/art/characters/hero.png")]
    public async Task A_root_contains_what_is_under_it(string path)
    {
        await Assert.That(ContainingRoot.Of(path, ["/wc"], Sensitive)).IsEqualTo("/wc");
    }

    /// <summary>
    /// Plain <c>StartsWith</c> gets this wrong, and getting it wrong routes a request for one
    /// working copy at another one's index.
    /// </summary>
    [Test]
    [Arguments("/wc2")]
    [Arguments("/wc2/art")]
    [Arguments("/wcx/art")]
    public async Task A_sibling_that_merely_starts_the_same_is_not_contained(string path)
    {
        await Assert.That(ContainingRoot.Of(path, ["/wc"], Sensitive)).IsNull();
    }

    /// <summary>A checkout inside another checkout answers for its own files, not its parent's.</summary>
    [Test]
    public async Task The_longest_containing_root_wins_whatever_order_they_come_in()
    {
        await Assert
            .That(ContainingRoot.Of("/wc/vendor/lib.dll", ["/wc", "/wc/vendor"], Sensitive))
            .IsEqualTo("/wc/vendor");
        await Assert
            .That(ContainingRoot.Of("/wc/vendor/lib.dll", ["/wc/vendor", "/wc"], Sensitive))
            .IsEqualTo("/wc/vendor");
    }

    [Test]
    public async Task A_path_outside_every_root_belongs_to_none_of_them()
    {
        await Assert.That(ContainingRoot.Of("/elsewhere", ["/wc", "/other"], Sensitive)).IsNull();
    }

    [Test]
    public async Task No_roots_at_all_is_not_a_match()
    {
        await Assert.That(ContainingRoot.Of("/wc/art", [], Sensitive)).IsNull();
    }

    [Test]
    [Arguments("/wc/")]
    [Arguments("/wc")]
    public async Task A_trailing_separator_on_the_root_changes_nothing(string root)
    {
        await Assert.That(ContainingRoot.Of("/wc/art", [root], Sensitive)).IsEqualTo(root);
    }

    /// <summary>
    /// A root of <c>/</c> trims to nothing, and an empty prefix matches every path there is — so
    /// the trim is undone rather than applied. A checkout at the filesystem root is unusual, not
    /// impossible, and the failure mode is every path in the world routing to it.
    /// </summary>
    [Test]
    [Arguments("/")]
    [Arguments("//")]
    public async Task A_root_that_is_nothing_but_separators_still_has_to_match_properly(string root)
    {
        await Assert.That(ContainingRoot.Of("/wc/art", [root], Sensitive)).IsEqualTo(root);
        await Assert.That(ContainingRoot.Of("wc/art", [root], Sensitive)).IsNull();
    }

    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, "/WC")]
    [Arguments(StringComparison.Ordinal, null)]
    public async Task Case_is_the_callers_decision_because_the_platforms_disagree(
        StringComparison comparison,
        string? expected
    )
    {
        await Assert.That(ContainingRoot.Of("/WC/art", ["/WC"], comparison)).IsEqualTo("/WC");
        await Assert.That(ContainingRoot.Of("/wc/art", ["/WC"], comparison)).IsEqualTo(expected);
    }

    [Test]
    public async Task The_platform_comparison_is_case_insensitive_only_on_windows()
    {
        await Assert
            .That(ContainingRoot.PlatformComparison)
            .IsEqualTo(
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            );
    }

    [Test]
    public async Task The_native_separator_is_accepted_as_the_boundary()
    {
        var root = Path.GetFullPath("/wc");
        var path = Path.Combine(root, "art");

        await Assert
            .That(ContainingRoot.Of(path, [root], ContainingRoot.PlatformComparison))
            .IsEqualTo(root);
    }
}
