using Subverted.Core;
using TUnit.Assertions.Enums;

namespace Subverted.Daemon.Tests;

public sealed class CommitSelectionPlannerTests
{
    private const StringComparison Ordinal = StringComparison.Ordinal;

    private static readonly UnrecordedMove HeroRenamed = new("art/hero.png", "art/protagonist.png");

    [Test]
    public async Task An_edit_is_committed_as_it_is_and_marks_nothing()
    {
        var plan = Plan(["art/hero.png"], [Entry("art/hero.png", NodeStatus.Modified)]);

        await Assert.That(plan.CommitTargets).IsEquivalentTo(["art/hero.png"]);
        await Assert.That(plan.Additions).IsEmpty();
        await Assert.That(plan.Deletions).IsEmpty();
        await Assert.That(plan.Moves).IsEmpty();
        await Assert.That(plan.Refusals).IsEmpty();
    }

    /// <summary>
    /// Every status a commit takes as it is. Clean is among them on purpose: an edit undone in the
    /// editor after it was ticked should send nothing, not refuse the rest of the commit.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Unmodified)]
    [Arguments(NodeStatus.NeedsPristineCompare)]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Deleted)]
    [Arguments(NodeStatus.Replaced)]
    public async Task A_status_svn_can_already_commit_needs_no_marking(NodeStatus status)
    {
        var plan = Plan(["a.txt"], [Entry("a.txt", status)]);

        await Assert.That(plan.CommitTargets).IsEquivalentTo(["a.txt"]);
        await Assert.That(plan.Additions).IsEmpty();
        await Assert.That(plan.Deletions).IsEmpty();
        await Assert.That(plan.Refusals).IsEmpty();
    }

    [Test]
    public async Task An_unversioned_node_is_added_and_committed()
    {
        var added = Entry("art/new.png", NodeStatus.Unversioned);

        var plan = Plan(["art/new.png"], [added]);

        await Assert.That(plan.Additions).IsEquivalentTo([added]);
        await Assert.That(plan.Deletions).IsEmpty();
        await Assert.That(plan.CommitTargets).IsEquivalentTo(["art/new.png"]);
    }

    [Test]
    public async Task A_missing_node_is_recorded_as_deleted_and_committed()
    {
        var plan = Plan(["art/old.png"], [Entry("art/old.png", NodeStatus.Missing)]);

        await Assert.That(plan.Deletions).IsEquivalentTo(["art/old.png"]);
        await Assert.That(plan.Additions).IsEmpty();
        await Assert.That(plan.CommitTargets).IsEquivalentTo(["art/old.png"]);
    }

    [Test]
    public async Task A_rename_with_both_halves_ticked_is_one_move_and_commits_both_halves()
    {
        var plan = Plan(
            ["art/hero.png", "art/protagonist.png"],
            [
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("art/protagonist.png", NodeStatus.Unversioned),
            ],
            [HeroRenamed]
        );

        await Assert.That(plan.Moves).IsEquivalentTo([HeroRenamed]);
        await Assert
            .That(plan.CommitTargets)
            .IsEquivalentTo(["art/hero.png", "art/protagonist.png"], CollectionOrdering.Matching);
        await Assert.That(plan.Refusals).IsEmpty();
    }

    /// <summary>
    /// The precedence that is the whole feature: each half of a pair reads as a plain <c>!</c> or
    /// <c>?</c>, and planned by its own status it would be the delete-and-add that ends history.
    /// </summary>
    [Test]
    public async Task A_paired_rename_is_not_also_planned_as_a_deletion_and_an_addition()
    {
        var plan = Plan(
            ["art/protagonist.png", "art/hero.png"],
            [
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("art/protagonist.png", NodeStatus.Unversioned),
            ],
            [HeroRenamed]
        );

        await Assert.That(plan.Additions).IsEmpty();
        await Assert.That(plan.Deletions).IsEmpty();
        await Assert.That(plan.Moves.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("art/hero.png")]
    [Arguments("art/protagonist.png")]
    public async Task Ticking_one_half_of_a_rename_refuses_the_request(string half)
    {
        var plan = Plan(
            [half, "readme.txt"],
            [
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("art/protagonist.png", NodeStatus.Unversioned),
                Entry("readme.txt", NodeStatus.Modified),
            ],
            [HeroRenamed]
        );

        await Assert
            .That(plan.Refusals)
            .IsEquivalentTo([
                "'art/hero.png' and 'art/protagonist.png' are one rename. Tick both to record it "
                    + "with its history, or neither.",
            ]);
        await Assert.That(plan.Moves).IsEmpty();
        await Assert.That(plan.Deletions).IsEmpty();
        await Assert.That(plan.Additions).IsEmpty();
    }

    /// <summary>A pair D27 found but nobody ticked is none of this request's business.</summary>
    [Test]
    public async Task A_rename_nobody_ticked_is_left_alone()
    {
        var plan = Plan(
            ["readme.txt"],
            [
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("art/protagonist.png", NodeStatus.Unversioned),
                Entry("readme.txt", NodeStatus.Modified),
            ],
            [HeroRenamed]
        );

        await Assert.That(plan.Moves).IsEmpty();
        await Assert.That(plan.Refusals).IsEmpty();
        await Assert.That(plan.CommitTargets).IsEquivalentTo(["readme.txt"]);
    }

    [Test]
    public async Task A_path_the_status_does_not_know_is_refused()
    {
        var plan = Plan(["art/gone.png"], [Entry("readme.txt", NodeStatus.Modified)]);

        await Assert
            .That(plan.Refusals)
            .IsEquivalentTo([
                "'art/gone.png' is neither versioned nor on disk — it may have gone since the list "
                    + "was shown.",
            ]);
        await Assert.That(plan.CommitTargets).IsEmpty();
    }

    [Test]
    [Arguments(
        NodeStatus.Obstructed,
        "'x' is versioned as one kind and is the other on disk. Put that right before committing it."
    )]
    [Arguments(
        NodeStatus.Ignored,
        "'x' is ignored. If it belongs in the repository, add it by hand first."
    )]
    [Arguments(
        NodeStatus.External,
        "'x' is an svn:externals checkout, a working copy of its own. Commit it from there."
    )]
    [Arguments(
        NodeStatus.Incomplete,
        "'x' is incomplete — an update was interrupted. Update it before committing."
    )]
    [Arguments(
        NodeStatus.Conflicted,
        "'x' is in conflict. Resolve it first — SVN will not commit it."
    )]
    public async Task A_node_svn_would_refuse_is_refused_with_the_reason(
        NodeStatus status,
        string reason
    )
    {
        var plan = Plan(["x"], [Entry("x", status)]);

        await Assert.That(plan.Refusals).IsEquivalentTo([reason]);
        await Assert.That(plan.CommitTargets).IsEmpty();
        await Assert.That(plan.Additions).IsEmpty();
        await Assert.That(plan.Deletions).IsEmpty();
    }

    /// <summary>
    /// The conflict flag is its own axis: a modified file with a tree or property conflict reads
    /// <c>M</c> on the content axis, and committing it would fail the whole commit.
    /// </summary>
    [Test]
    public async Task A_conflict_flag_refuses_a_node_whatever_its_content_status()
    {
        var plan = Plan(["x"], [Entry("x", NodeStatus.Modified) with { IsConflicted = true }]);

        await Assert
            .That(plan.Refusals)
            .IsEquivalentTo(["'x' is in conflict. Resolve it first — SVN will not commit it."]);
    }

    [Test]
    public async Task One_refusal_is_reported_alongside_everything_that_was_fine()
    {
        var plan = Plan(
            ["ok.txt", "x"],
            [Entry("ok.txt", NodeStatus.Modified), Entry("x", NodeStatus.Ignored)]
        );

        await Assert.That(plan.Refusals.Count).IsEqualTo(1);
        await Assert.That(plan.CommitTargets).IsEquivalentTo(["ok.txt"]);
    }

    [Test]
    public async Task A_path_ticked_twice_is_planned_once()
    {
        var plan = Plan(["new.txt", "new.txt"], [Entry("new.txt", NodeStatus.Unversioned)]);

        await Assert.That(plan.Additions.Count).IsEqualTo(1);
        await Assert.That(plan.CommitTargets).IsEquivalentTo(["new.txt"]);
    }

    /// <summary>
    /// Paths compare as the platform compares them. On Windows the front-end may spell a ticked
    /// path in another case than wc.db does, and it is still the same file.
    /// </summary>
    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, true)]
    [Arguments(StringComparison.Ordinal, false)]
    public async Task Paths_match_as_the_platform_compares_them(
        StringComparison comparison,
        bool matches
    )
    {
        var plan = CommitSelectionPlanner.Plan(
            ["ART/HERO.PNG", "art/protagonist.png"],
            [
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("art/protagonist.png", NodeStatus.Unversioned),
            ],
            [HeroRenamed],
            comparison
        );

        await Assert.That(plan.Moves.Count).IsEqualTo(matches ? 1 : 0);
        await Assert.That(plan.Refusals.Count).IsEqualTo(matches ? 0 : 2);
    }

    [Test]
    public async Task A_mixed_selection_puts_each_node_where_it_belongs_in_ticked_order()
    {
        var added = Entry("art/new.png", NodeStatus.Unversioned);

        var plan = Plan(
            ["readme.txt", "art/new.png", "art/old.png", "art/protagonist.png", "art/hero.png"],
            [
                Entry("readme.txt", NodeStatus.Modified),
                added,
                Entry("art/old.png", NodeStatus.Missing),
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("art/protagonist.png", NodeStatus.Unversioned),
            ],
            [HeroRenamed]
        );

        await Assert.That(plan.Additions).IsEquivalentTo([added]);
        await Assert.That(plan.Deletions).IsEquivalentTo(["art/old.png"]);
        await Assert.That(plan.Moves).IsEquivalentTo([HeroRenamed]);
        await Assert
            .That(plan.CommitTargets)
            .IsEquivalentTo(
                ["readme.txt", "art/new.png", "art/old.png", "art/protagonist.png", "art/hero.png"],
                CollectionOrdering.Matching
            );
    }

    private static CommitSelectionPlan Plan(
        IReadOnlyList<string> ticked,
        IReadOnlyList<WorkingCopyEntry> entries,
        IReadOnlyList<UnrecordedMove>? moves = null
    ) => CommitSelectionPlanner.Plan(ticked, entries, moves ?? [], Ordinal);

    private static WorkingCopyEntry Entry(string relPath, NodeStatus status) =>
        new(
            relPath,
            NodeKind.File,
            status,
            PropertyStatus.Unmodified,
            Revision: status is NodeStatus.Unversioned ? null : 1,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );
}
