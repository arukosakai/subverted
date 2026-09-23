using Subverted.Core;

namespace Subverted.Daemon.Tests;

/// <summary>
/// <c>sv st PATH...</c>, measured against <c>svn status PATH...</c> on SVN 1.8.15: a file lists
/// that node, a directory lists itself and everything beneath it, several targets list the union,
/// and a path nothing is at lists nothing.
/// </summary>
public sealed class StatusScopeTests
{
    private static readonly IReadOnlyList<WorkingCopyEntry> Tree =
    [
        Entry(string.Empty),
        Entry("art"),
        Entry("art/hero.png"),
        Entry("art/ui/button.png"),
        Entry("artwork/sketch.png"),
        Entry("readme.txt"),
    ];

    [Test]
    public async Task No_scope_lists_the_whole_working_copy()
    {
        await Assert
            .That(Paths(StatusFilter.Within(Tree, scope: null, StringComparison.Ordinal)))
            .IsEquivalentTo(Tree.Select(entry => entry.RelPath));
    }

    [Test]
    public async Task A_directory_lists_itself_and_everything_beneath_it()
    {
        await Assert
            .That(Paths(StatusFilter.Within(Tree, ["art"], StringComparison.Ordinal)))
            .IsEquivalentTo(new[] { "art", "art/hero.png", "art/ui/button.png" });
    }

    /// <summary>
    /// <c>artwork</c> starts with <c>art</c>. A prefix test that forgot the separator would list
    /// a sibling folder under a target it has nothing to do with.
    /// </summary>
    [Test]
    public async Task A_sibling_sharing_the_targets_prefix_is_not_listed()
    {
        await Assert
            .That(Paths(StatusFilter.Within(Tree, ["art"], StringComparison.Ordinal)))
            .DoesNotContain("artwork/sketch.png");
    }

    [Test]
    public async Task A_file_lists_that_node_alone()
    {
        await Assert
            .That(Paths(StatusFilter.Within(Tree, ["art/hero.png"], StringComparison.Ordinal)))
            .IsEquivalentTo(new[] { "art/hero.png" });
    }

    [Test]
    public async Task Several_targets_list_the_union()
    {
        await Assert
            .That(
                Paths(StatusFilter.Within(Tree, ["art/ui", "readme.txt"], StringComparison.Ordinal))
            )
            .IsEquivalentTo(new[] { "art/ui/button.png", "readme.txt" });
    }

    [Test]
    public async Task The_root_lists_everything()
    {
        await Assert
            .That(StatusFilter.Within(Tree, [string.Empty], StringComparison.Ordinal).Count)
            .IsEqualTo(Tree.Count);
    }

    [Test]
    public async Task A_target_nothing_is_at_lists_nothing()
    {
        await Assert
            .That(StatusFilter.Within(Tree, ["nosuch.txt"], StringComparison.Ordinal))
            .IsEmpty();
    }

    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, 3)]
    [Arguments(StringComparison.Ordinal, 0)]
    public async Task Case_is_compared_the_way_the_platform_compares_it(
        StringComparison comparison,
        int expected
    )
    {
        await Assert.That(StatusFilter.Within(Tree, ["ART"], comparison).Count).IsEqualTo(expected);
    }

    /// <summary>
    /// A rename's note explains two lines of the listing, and one half can fall outside the target
    /// — a file dragged into another folder. Either half in view keeps the pair, so the note never
    /// explains a line that is not there and never goes missing for one that is.
    /// </summary>
    [Test]
    [Arguments("art", 1)]
    [Arguments("sprites", 1)]
    [Arguments("readme.txt", 0)]
    public async Task A_rename_is_kept_when_either_half_is_in_scope(string target, int expected)
    {
        UnrecordedMove[] moves = [new("art/hero.png", "sprites/hero.png")];

        await Assert
            .That(StatusFilter.MovesWithin(moves, [target], StringComparison.Ordinal).Count)
            .IsEqualTo(expected);
    }

    [Test]
    public async Task No_scope_keeps_every_rename()
    {
        UnrecordedMove[] moves = [new("art/hero.png", "sprites/hero.png")];

        await Assert
            .That(StatusFilter.MovesWithin(moves, scope: null, StringComparison.Ordinal))
            .IsEquivalentTo(moves);
    }

    private static IEnumerable<string> Paths(IReadOnlyList<WorkingCopyEntry> entries) =>
        entries.Select(entry => entry.RelPath);

    private static WorkingCopyEntry Entry(string relPath) =>
        new(
            relPath,
            NodeKind.File,
            NodeStatus.Modified,
            PropertyStatus.Unmodified,
            Revision: 1,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );
}
