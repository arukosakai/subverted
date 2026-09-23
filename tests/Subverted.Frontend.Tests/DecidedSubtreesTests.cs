using Subverted.Core;

namespace Subverted.Frontend.Tests;

/// <summary>
/// D20's directory rules on their own, apart from any walk that asks about them. Each rule is
/// tested from both sides of the answer that triggers it, because "never offer children" and
/// "always offer children" are both wrong and both easy to write.
/// </summary>
public sealed class DecidedSubtreesTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    [Test]
    public async Task Before_any_answer_nothing_is_decided()
    {
        var decided = new DecidedSubtrees(Sensitive);

        await Assert.That(decided.Decides(Added("fresh/n.txt"))).IsFalse();
        await Assert.That(decided.Decides(Deleted("art/hero.png"))).IsFalse();
    }

    /// <summary>SVN refuses a child whose added parent is not in the same commit (E200009).</summary>
    [Test]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Replaced)]
    public async Task Leaving_an_added_directory_out_rules_out_everything_below_it(
        NodeStatus status
    )
    {
        var decided = new DecidedSubtrees(Sensitive);
        decided.Left(Directory("fresh", status));

        await Assert.That(decided.Decides(Added("fresh/n.txt"))).IsTrue();
        await Assert.That(decided.Decides(Modified("fresh/m.txt"))).IsTrue();
    }

    [Test]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Replaced)]
    public async Task Sending_an_added_directory_leaves_each_addition_below_it_a_choice(
        NodeStatus status
    )
    {
        var decided = new DecidedSubtrees(Sensitive);
        decided.Sent(Directory("fresh", status));

        await Assert.That(decided.Decides(Added("fresh/n.txt"))).IsFalse();
    }

    /// <summary>A deletion is recorded on the directory, so its deletions follow it either way.</summary>
    [Test]
    [Arguments("sent")]
    [Arguments("left")]
    public async Task A_deleted_directory_carries_the_deletions_below_it_whichever_way_it_goes(
        string answer
    )
    {
        var decided = new DecidedSubtrees(Sensitive);
        Answer(decided, Directory("art", NodeStatus.Deleted), answer);

        await Assert.That(decided.Decides(Deleted("art/hero.png"))).IsTrue();
    }

    /// <summary>
    /// A deleted directory only carries deletions. Anything else below it is not carried — the
    /// rule is about the deletion, which is what makes a replaced directory work.
    /// </summary>
    [Test]
    public async Task A_sent_deleted_directory_does_not_decide_what_is_not_a_deletion()
    {
        var decided = new DecidedSubtrees(Sensitive);
        decided.Sent(Directory("art", NodeStatus.Deleted));

        await Assert.That(decided.Decides(Added("art/new.png"))).IsFalse();
    }

    [Test]
    public async Task A_sent_replaced_directory_carries_its_deletions_and_leaves_its_additions_a_choice()
    {
        var decided = new DecidedSubtrees(Sensitive);
        decided.Sent(Directory("lib", NodeStatus.Replaced));

        await Assert.That(decided.Decides(Deleted("lib/old.txt"))).IsTrue();
        await Assert.That(decided.Decides(Added("lib/new.txt"))).IsFalse();
    }

    [Test]
    public async Task A_replaced_directory_left_out_decides_both_its_deletions_and_its_additions()
    {
        var decided = new DecidedSubtrees(Sensitive);
        decided.Left(Directory("lib", NodeStatus.Replaced));

        await Assert.That(decided.Decides(Deleted("lib/old.txt"))).IsTrue();
        await Assert.That(decided.Decides(Added("lib/new.txt"))).IsTrue();
    }

    /// <summary>Only directories carry a subtree. A file with the same status settles nothing.</summary>
    [Test]
    [Arguments(NodeStatus.Added, "left")]
    [Arguments(NodeStatus.Replaced, "left")]
    [Arguments(NodeStatus.Deleted, "left")]
    [Arguments(NodeStatus.Deleted, "sent")]
    [Arguments(NodeStatus.Replaced, "sent")]
    public async Task A_file_settles_nothing_whatever_its_status(NodeStatus status, string answer)
    {
        var decided = new DecidedSubtrees(Sensitive);
        Answer(decided, Modified("art") with { Status = status }, answer);

        await Assert.That(decided.Decides(Deleted("art/x"))).IsFalse();
        await Assert.That(decided.Decides(Added("art/y"))).IsFalse();
    }

    /// <summary>The rules are about added and deleted directories, not about directories.</summary>
    [Test]
    [Arguments(NodeStatus.Unmodified)]
    [Arguments(NodeStatus.Modified)]
    public async Task A_directory_that_was_neither_added_nor_deleted_settles_nothing(
        NodeStatus status
    )
    {
        var decided = new DecidedSubtrees(Sensitive);
        decided.Left(Directory("src", status) with { PropertyStatus = PropertyStatus.Modified });

        await Assert.That(decided.Decides(Modified("src/a.txt"))).IsFalse();
        await Assert.That(decided.Decides(Deleted("src/b.txt"))).IsFalse();
    }

    [Test]
    public async Task The_rules_reach_every_level_below_and_not_only_the_children()
    {
        var decided = new DecidedSubtrees(Sensitive);
        decided.Left(Directory("fresh", NodeStatus.Added));
        decided.Sent(Directory("art", NodeStatus.Deleted));

        await Assert.That(decided.Decides(Added("fresh/deep/d.txt"))).IsTrue();
        await Assert.That(decided.Decides(Deleted("art/deep/hero.png"))).IsTrue();
    }

    /// <summary>A directory's answer decides what is below it, never the directory itself.</summary>
    [Test]
    public async Task A_directory_does_not_decide_itself()
    {
        var decided = new DecidedSubtrees(Sensitive);
        var fresh = Directory("fresh", NodeStatus.Added);
        var art = Directory("art", NodeStatus.Deleted);
        decided.Left(fresh);
        decided.Left(art);

        await Assert.That(decided.Decides(fresh)).IsFalse();
        await Assert.That(decided.Decides(art)).IsFalse();
    }

    /// <summary>Declining <c>art</c> must not take <c>artefacts</c> with it.</summary>
    [Test]
    public async Task A_sibling_whose_name_starts_with_the_directorys_is_not_decided()
    {
        var decided = new DecidedSubtrees(Sensitive);
        decided.Left(Directory("art", NodeStatus.Replaced));

        await Assert.That(decided.Decides(Added("artefacts/notes.txt"))).IsFalse();
        await Assert.That(decided.Decides(Deleted("artefacts/old.txt"))).IsFalse();
    }

    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, true)]
    [Arguments(StringComparison.Ordinal, false)]
    public async Task Paths_compare_as_the_platform_compares_them(
        StringComparison comparison,
        bool decides
    )
    {
        var decided = new DecidedSubtrees(comparison);
        decided.Left(Directory("fresh", NodeStatus.Added));
        decided.Left(Directory("art", NodeStatus.Deleted));

        await Assert.That(decided.Decides(Added("FRESH/n.txt"))).IsEqualTo(decides);
        await Assert.That(decided.Decides(Deleted("ART/hero.png"))).IsEqualTo(decides);
    }

    private static void Answer(DecidedSubtrees decided, WorkingCopyEntry entry, string answer)
    {
        if (answer == "sent")
        {
            decided.Sent(entry);
        }
        else
        {
            decided.Left(entry);
        }
    }

    private static WorkingCopyEntry Modified(string relPath) =>
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

    private static WorkingCopyEntry Added(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Added,
        };

    private static WorkingCopyEntry Deleted(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Deleted,
        };

    private static WorkingCopyEntry Directory(string relPath, NodeStatus status) =>
        Modified(relPath) with
        {
            Kind = NodeKind.Directory,
            Status = status,
        };
}
