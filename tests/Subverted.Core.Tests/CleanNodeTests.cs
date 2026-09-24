namespace Subverted.Core.Tests;

/// <summary>
/// Which nodes <c>svn status</c> prints without <c>-v</c>. Each rule is flipped on an otherwise
/// clean node, so every column that can make a node worth reporting is tested on its own.
/// </summary>
public sealed class CleanNodeTests
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
    public async Task A_node_unchanged_in_content_and_properties_with_nothing_held_is_clean()
    {
        await Assert.That(CleanNode.Is(Untouched)).IsTrue();
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
    public async Task Any_content_or_tree_status_but_unmodified_is_not_clean(NodeStatus status)
    {
        await Assert.That(CleanNode.Is(Untouched with { Status = status })).IsFalse();
    }

    /// <summary>svn status's <c>" M"</c>: a commit sends it, so a listing of changes must hold it.</summary>
    [Test]
    public async Task Changed_properties_alone_are_not_clean()
    {
        var propsOnly = Untouched with { PropertyStatus = PropertyStatus.Modified };

        await Assert.That(CleanNode.Is(propsOnly)).IsFalse();
    }

    [Test]
    public async Task A_conflict_on_clean_content_is_not_clean()
    {
        await Assert.That(CleanNode.Is(Untouched with { IsConflicted = true })).IsFalse();
    }

    [Test]
    public async Task A_held_lock_on_clean_content_is_not_clean()
    {
        await Assert.That(CleanNode.Is(Untouched with { HasLockToken = true })).IsFalse();
    }

    [Test]
    public async Task A_write_locked_directory_is_not_clean()
    {
        var wedged = Untouched with { Kind = NodeKind.Directory, IsWriteLocked = true };

        await Assert.That(CleanNode.Is(wedged)).IsFalse();
    }

    /// <summary><c>svn status</c> hides an untouched file of a copied folder, and so does this.</summary>
    [Test]
    public async Task An_untouched_node_carried_by_a_copy_is_still_clean()
    {
        await Assert.That(CleanNode.Is(Untouched with { IsCopied = true })).IsTrue();
    }
}
