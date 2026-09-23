using Subverted.Core;

namespace Subverted.Daemon.Tests;

/// <summary>
/// What a status listing carries. Every rule here is one that <c>svn status</c> follows, and the
/// two switches are tested against each other rather than only on their own.
/// </summary>
public sealed class StatusFilterTests
{
    [Test]
    [Arguments(false, 0)]
    [Arguments(true, 1)]
    public async Task A_node_with_nothing_to_report_is_carried_only_when_asked_for(
        bool includeUnmodified,
        int expected
    )
    {
        var entries = StatusFilter.Apply([Clean()], includeUnmodified, includeIgnored: false);

        await Assert.That(entries.Count).IsEqualTo(expected);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_modified_node_is_carried_either_way(bool includeUnmodified)
    {
        var entries = StatusFilter.Apply(
            [Clean() with { Status = NodeStatus.Modified }],
            includeUnmodified,
            includeIgnored: false
        );

        await Assert.That(entries.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The D9 case. Content-clean and property-dirty is <c>svn status</c>'s <c>" M"</c>, and a
    /// listing that drops it is telling someone there is nothing to commit when there is.
    /// </summary>
    [Test]
    public async Task A_node_that_is_dirty_only_on_properties_is_still_worth_reporting()
    {
        var entries = StatusFilter.Apply(
            [Clean() with { PropertyStatus = PropertyStatus.Modified }],
            includeUnmodified: false,
            includeIgnored: false
        );

        await Assert.That(entries.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Both print in their own column even when content and properties are clean, so neither is
    /// "nothing to report".
    /// </summary>
    [Test]
    public async Task A_conflict_or_a_held_lock_is_reported_on_an_otherwise_clean_node()
    {
        var conflicted = Clean() with { IsConflicted = true };
        var locked = Clean() with { HasLockToken = true };

        var entries = StatusFilter.Apply(
            [conflicted, locked],
            includeUnmodified: false,
            includeIgnored: false
        );

        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries[0]).IsEqualTo(conflicted);
        await Assert.That(entries[1]).IsEqualTo(locked);
    }

    /// <summary>
    /// The one that would be hidden exactly when it matters. A write-locked directory is almost
    /// always otherwise unmodified, and it is the reason every write to this working copy is about
    /// to fail — filtered out, <c>sv st</c> reports a wedged copy as clean.
    /// </summary>
    [Test]
    public async Task A_write_locked_directory_is_reported_though_nothing_about_it_changed()
    {
        var wedged = Clean() with { IsWriteLocked = true };

        var entries = StatusFilter.Apply([wedged], includeUnmodified: false, includeIgnored: false);

        await Assert.That(entries).IsEquivalentTo(new[] { wedged });
    }

    /// <summary>
    /// The opposite of the write lock, and just as deliberate. <c>svn status</c> hides an untouched
    /// file inside a copied directory and shows it only under <c>-v</c>; carrying it would list
    /// every file of a copied folder, which is the noise this column's arrival cleared up.
    /// </summary>
    [Test]
    [Arguments(false, 0)]
    [Arguments(true, 1)]
    public async Task An_untouched_copied_node_is_carried_only_in_a_verbose_listing(
        bool includeUnmodified,
        int expected
    )
    {
        var copied = Clean() with { IsCopied = true };

        var entries = StatusFilter.Apply([copied], includeUnmodified, includeIgnored: false);

        await Assert.That(entries.Count).IsEqualTo(expected);
    }

    [Test]
    [Arguments(false, 0)]
    [Arguments(true, 1)]
    public async Task An_ignored_node_is_carried_only_when_asked_for(
        bool includeIgnored,
        int expected
    )
    {
        var entries = StatusFilter.Apply(
            [Clean() with { Status = NodeStatus.Ignored }],
            includeUnmodified: false,
            includeIgnored
        );

        await Assert.That(entries.Count).IsEqualTo(expected);
    }

    /// <summary>
    /// The precedence, not each rule on its own. <c>-v</c> means "show me the clean ones too",
    /// never "show me the build tree", and an artist who asked for one and got the other stops
    /// reading status output.
    /// </summary>
    [Test]
    public async Task Asking_for_unmodified_nodes_does_not_let_ignored_ones_through()
    {
        var entries = StatusFilter.Apply(
            [Clean() with { Status = NodeStatus.Ignored }],
            includeUnmodified: true,
            includeIgnored: false
        );

        await Assert.That(entries).IsEmpty();
    }

    [Test]
    public async Task An_unversioned_node_is_not_an_unmodified_one_and_is_always_carried()
    {
        var entries = StatusFilter.Apply(
            [Clean() with { Status = NodeStatus.Unversioned }],
            includeUnmodified: false,
            includeIgnored: false
        );

        await Assert.That(entries.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Order_is_the_scans_order_and_is_not_rearranged()
    {
        var first = Clean() with { RelPath = "a", Status = NodeStatus.Modified };
        var second = Clean() with { RelPath = "b", Status = NodeStatus.Added };

        var entries = StatusFilter.Apply(
            [first, second],
            includeUnmodified: false,
            includeIgnored: false
        );

        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries[0]).IsEqualTo(first);
        await Assert.That(entries[1]).IsEqualTo(second);
    }

    private static WorkingCopyEntry Clean() =>
        new(
            "art/hero.png",
            NodeKind.File,
            NodeStatus.Unmodified,
            PropertyStatus.Unmodified,
            Revision: 42,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );
}
