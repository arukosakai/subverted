using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class ChangeBadgesTests
{
    [Test]
    [Arguments(NodeStatus.Modified, "Modified", ChangeTone.Modified)]
    [Arguments(NodeStatus.Added, "Added", ChangeTone.Added)]
    [Arguments(NodeStatus.Deleted, "Deleted", ChangeTone.Deleted)]
    [Arguments(NodeStatus.Replaced, "Replaced", ChangeTone.Replaced)]
    [Arguments(NodeStatus.Conflicted, "Conflict", ChangeTone.Conflict)]
    [Arguments(NodeStatus.Missing, "Missing", ChangeTone.Missing)]
    [Arguments(NodeStatus.Incomplete, "Incomplete", ChangeTone.Missing)]
    [Arguments(NodeStatus.Obstructed, "Obstructed", ChangeTone.Missing)]
    [Arguments(NodeStatus.Unversioned, "Not versioned", ChangeTone.Unversioned)]
    [Arguments(NodeStatus.Ignored, "Ignored", ChangeTone.Quiet)]
    [Arguments(NodeStatus.External, "External", ChangeTone.Quiet)]
    [Arguments(NodeStatus.NeedsPristineCompare, "Undecided", ChangeTone.Quiet)]
    [Arguments(NodeStatus.Unmodified, "Unchanged", ChangeTone.Quiet)]
    public async Task Each_status_has_its_own_badge(
        NodeStatus status,
        string label,
        ChangeTone tone
    )
    {
        await Assert
            .That(ChangeBadges.For(Entry("a.png", status)))
            .IsEqualTo(new ChangeBadge(label, tone));
    }

    /// <summary>
    /// A property conflict leaves the content axis reading as modified; the conflict is what the
    /// person has to deal with first, so it is what the badge says.
    /// </summary>
    [Test]
    public async Task A_conflict_outranks_what_the_content_axis_says()
    {
        var badge = ChangeBadges.For(Entry("a.png", NodeStatus.Modified, isConflicted: true));

        await Assert.That(badge).IsEqualTo(new ChangeBadge("Conflict", ChangeTone.Conflict));
    }

    [Test]
    [Arguments(true, "Copied")]
    [Arguments(false, "Added")]
    public async Task An_add_says_whether_it_came_with_history(bool isCopied, string label)
    {
        var badge = ChangeBadges.For(Entry("a.png", NodeStatus.Added, isCopied: isCopied));

        await Assert.That(badge.Label).IsEqualTo(label);
    }

    /// <summary>
    /// A property-only change reads as unmodified on the content axis and is still something a
    /// commit sends — calling it "Unchanged" would hide it.
    /// </summary>
    [Test]
    [Arguments(PropertyStatus.Modified, "Properties", ChangeTone.Modified)]
    [Arguments(PropertyStatus.Unmodified, "Unchanged", ChangeTone.Quiet)]
    public async Task An_unchanged_file_with_changed_properties_is_a_change(
        PropertyStatus properties,
        string label,
        ChangeTone tone
    )
    {
        var badge = ChangeBadges.For(Entry("a.png", NodeStatus.Unmodified, properties));

        await Assert.That(badge).IsEqualTo(new ChangeBadge(label, tone));
    }
}
