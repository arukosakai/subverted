using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>Lock is offered where SVN 1.8.15 was seen to grant one, and Unlock only where it is held.</summary>
public sealed class LockOfferTests
{
    [Test]
    [Arguments(NodeStatus.Modified, true)]
    [Arguments(NodeStatus.Unmodified, true)]
    [Arguments(NodeStatus.NeedsPristineCompare, true)]
    [Arguments(NodeStatus.Missing, true)]
    [Arguments(NodeStatus.Deleted, true)]
    [Arguments(NodeStatus.Replaced, true)]
    [Arguments(NodeStatus.Obstructed, true)]
    [Arguments(NodeStatus.Conflicted, true)]
    [Arguments(NodeStatus.Incomplete, true)]
    [Arguments(NodeStatus.Added, false)]
    [Arguments(NodeStatus.Unversioned, false)]
    [Arguments(NodeStatus.Ignored, false)]
    [Arguments(NodeStatus.External, false)]
    public async Task A_file_is_lockable_only_where_the_repository_has_it(
        NodeStatus status,
        bool expected
    )
    {
        var row = ChangeRow.From(Entry("art/hero.png", status));

        await Assert.That(LockOffer.CanLock(row)).IsEqualTo(expected);
    }

    /// <summary>E155010 on 1.8.15 for a copy's file, edited or not: the repository has nothing at the new path.</summary>
    [Test]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Modified)]
    public async Task A_copied_file_is_not_lockable(NodeStatus status)
    {
        var copied = ChangeRow.From(Entry("art/hero.png", status, isCopied: true));

        await Assert.That(LockOffer.CanLock(copied)).IsFalse();
    }

    [Test]
    [Arguments(NodeKind.Directory, false)]
    [Arguments(NodeKind.Symlink, false)]
    [Arguments(NodeKind.Unknown, false)]
    [Arguments(NodeKind.File, true)]
    public async Task Only_a_file_is_lockable(NodeKind kind, bool expected)
    {
        var row = ChangeRow.From(Entry("art", NodeStatus.Modified, kind: kind));

        await Assert.That(LockOffer.CanLock(row)).IsEqualTo(expected);
    }

    [Test]
    public async Task A_file_already_locked_here_is_not_offered_the_lock_again()
    {
        var held = ChangeRow.From(Entry("art/hero.png", hasLockToken: true));

        await Assert.That(LockOffer.CanLock(held)).IsFalse();
    }

    [Test]
    public async Task A_rename_row_is_not_lockable_since_its_new_path_is_unversioned()
    {
        var rename = ChangeRow.Rename(Entry("art/new.png", NodeStatus.Unversioned), "art/old.png");

        await Assert.That(LockOffer.CanLock(rename)).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Unlock_is_offered_exactly_where_the_lock_is_held(bool held)
    {
        var row = ChangeRow.From(Entry("art/hero.png", NodeStatus.Unmodified, hasLockToken: held));

        await Assert.That(LockOffer.CanUnlock(row)).IsEqualTo(held);
    }
}
