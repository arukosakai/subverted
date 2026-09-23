using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class RevertLossTests
{
    [Test]
    [Arguments(NodeStatus.Modified, "Edits are thrown away", true)]
    [Arguments(NodeStatus.Replaced, "The replacement is thrown away, the original put back", true)]
    [Arguments(
        NodeStatus.Obstructed,
        "Whatever is in its place on disk is deleted, and it is put back as last updated",
        true
    )]
    [Arguments(NodeStatus.Incomplete, "Any local changes are thrown away", true)]
    [Arguments(NodeStatus.NeedsPristineCompare, "Any local changes are thrown away", true)]
    [Arguments(NodeStatus.Conflicted, "Edits and the conflict are thrown away", true)]
    [Arguments(NodeStatus.Deleted, "Put back as it was at the last update", false)]
    [Arguments(NodeStatus.Missing, "Put back as it was at the last update", false)]
    [Arguments(NodeStatus.Added, "No longer added; what is on disk stays, not versioned", false)]
    public async Task Each_status_says_what_revert_does_and_whether_work_is_lost(
        NodeStatus status,
        string what,
        bool losesWork
    )
    {
        var line = RevertLoss.For(ChangeRow.From(Entry("a.png", status)));

        await Assert.That(line).IsEqualTo(new RevertLine("a.png", what, losesWork));
    }

    /// <summary>D19: revert never deletes what SVN does not version, nor releases a lock.</summary>
    [Test]
    [Arguments(NodeStatus.Unversioned)]
    [Arguments(NodeStatus.Ignored)]
    [Arguments(NodeStatus.External)]
    [Arguments(NodeStatus.Unmodified)]
    public async Task What_revert_leaves_alone_has_no_line(NodeStatus status)
    {
        var row = ChangeRow.From(Entry("a.png", status, hasLockToken: true));

        await Assert.That(RevertLoss.For(row)).IsNull();
    }

    [Test]
    [Arguments(NodeStatus.Modified, "Edits and property changes are thrown away")]
    [Arguments(NodeStatus.Unmodified, "Property changes are thrown away")]
    public async Task Property_changes_are_named_as_lost_too(NodeStatus status, string what)
    {
        var row = ChangeRow.From(Entry("a.png", status, PropertyStatus.Modified));

        await Assert.That(RevertLoss.For(row)).IsEqualTo(new RevertLine("a.png", what, true));
    }

    /// <summary>Measured on 1.8.15: unlike a plain add, a reverted copy is deleted from disk.</summary>
    [Test]
    public async Task A_copy_is_deleted_from_disk_and_that_is_lost_work()
    {
        var row = ChangeRow.From(Entry("a.png", NodeStatus.Added, isCopied: true));

        await Assert
            .That(RevertLoss.For(row))
            .IsEqualTo(
                new RevertLine(
                    "a.png",
                    "The copy is deleted from disk, with any edits and anything unversioned inside it",
                    true
                )
            );
    }

    /// <summary>The conflict flag outranks the content axis, as it does on the badge.</summary>
    [Test]
    public async Task A_conflicted_edit_is_reported_as_the_conflict()
    {
        var row = ChangeRow.From(Entry("a.png", NodeStatus.Modified, isConflicted: true));

        await Assert
            .That(RevertLoss.For(row)!.What)
            .IsEqualTo("Edits and the conflict are thrown away");
    }

    /// <summary>Only the old path is versioned; the new file is never deleted.</summary>
    [Test]
    public async Task A_rename_puts_its_old_path_back_and_leaves_the_new_file()
    {
        var row = ChangeRow.Rename(Entry("art/new.png", NodeStatus.Unversioned), "art/old.png");

        await Assert
            .That(RevertLoss.For(row))
            .IsEqualTo(
                new RevertLine(
                    "art/old.png",
                    "Put back as it was at the last update; the renamed file, art/new.png, is left as it is",
                    false
                )
            );
    }
}
