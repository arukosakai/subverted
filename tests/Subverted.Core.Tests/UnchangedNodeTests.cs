namespace Subverted.Core.Tests;

/// <summary>
/// Which nodes hold nothing to commit or revert. Each rule is flipped on an otherwise untouched
/// node, and the two lock marks are tested both here and against <see cref="CleanNode"/>, since
/// they are exactly where the two rules part.
/// </summary>
public sealed class UnchangedNodeTests
{
    private static readonly WorkingCopyEntry Untouched = new(
        "art/hero.png",
        NodeKind.File,
        NodeStatus.Unmodified,
        PropertyStatus.Unmodified,
        Revision: 7,
        Changelist: null,
        IsConflicted: false,
        HasLockToken: false,
        IsWriteLocked: false,
        IsCopied: false
    );

    [Test]
    public async Task A_node_unchanged_in_content_and_properties_is_unchanged()
    {
        await Assert.That(UnchangedNode.Is(Untouched)).IsTrue();
    }

    [Test]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Deleted)]
    [Arguments(NodeStatus.Missing)]
    [Arguments(NodeStatus.Unversioned)]
    [Arguments(NodeStatus.Ignored)]
    [Arguments(NodeStatus.External)]
    [Arguments(NodeStatus.NeedsPristineCompare)]
    public async Task Any_content_or_tree_status_but_unmodified_is_a_change(NodeStatus status)
    {
        await Assert.That(UnchangedNode.Is(Untouched with { Status = status })).IsFalse();
    }

    [Test]
    public async Task Changed_properties_alone_are_a_change()
    {
        var propsOnly = Untouched with { PropertyStatus = PropertyStatus.Modified };

        await Assert.That(UnchangedNode.Is(propsOnly)).IsFalse();
    }

    /// <summary>A conflict holds the commit back until it is settled, so it is never nothing.</summary>
    [Test]
    public async Task A_conflict_on_untouched_content_is_a_change()
    {
        await Assert.That(UnchangedNode.Is(Untouched with { IsConflicted = true })).IsFalse();
    }

    /// <summary>
    /// The lock an artist takes before starting: svn status still prints it, so it is not clean,
    /// but there is nothing in it to commit.
    /// </summary>
    [Test]
    public async Task A_held_lock_on_untouched_content_is_unchanged_but_not_clean()
    {
        var held = Untouched with { HasLockToken = true };

        await Assert.That(UnchangedNode.Is(held)).IsTrue();
        await Assert.That(CleanNode.Is(held)).IsFalse();
    }

    [Test]
    public async Task A_held_lock_on_edited_content_is_a_change()
    {
        var heldAndEdited = Untouched with { Status = NodeStatus.Modified, HasLockToken = true };

        await Assert.That(UnchangedNode.Is(heldAndEdited)).IsFalse();
    }

    [Test]
    public async Task A_write_locked_directory_is_unchanged_but_not_clean()
    {
        var wedged = Untouched with { Kind = NodeKind.Directory, IsWriteLocked = true };

        await Assert.That(UnchangedNode.Is(wedged)).IsTrue();
        await Assert.That(CleanNode.Is(wedged)).IsFalse();
    }
}
