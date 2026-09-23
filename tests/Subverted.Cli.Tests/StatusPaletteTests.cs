using Subverted.Core;

namespace Subverted.Cli.Tests;

public sealed class StatusPaletteTests
{
    private const char Escape = '\e';

    [Test]
    public async Task Plain_hands_the_line_back_untouched()
    {
        await Assert
            .That(StatusPalette.Plain("M  art/hero.png", Entry(NodeStatus.Modified)))
            .IsEqualTo("M  art/hero.png");
    }

    [Test]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.NeedsPristineCompare)]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Deleted)]
    [Arguments(NodeStatus.Missing)]
    [Arguments(NodeStatus.Incomplete)]
    [Arguments(NodeStatus.Replaced)]
    [Arguments(NodeStatus.Conflicted)]
    [Arguments(NodeStatus.Unversioned)]
    [Arguments(NodeStatus.Ignored)]
    public async Task Anything_worth_noticing_is_coloured_and_reset_afterwards(NodeStatus status)
    {
        var painted = StatusPalette.Ansi("line", Entry(status));

        await Assert.That(painted).StartsWith($"{Escape}[");
        await Assert.That(painted).EndsWith($"{Escape}[0m");
        await Assert.That(painted).Contains("line");
    }

    [Test]
    public async Task A_node_with_nothing_to_report_is_left_uncoloured()
    {
        await Assert
            .That(StatusPalette.Ansi("line", Entry(NodeStatus.Unmodified)))
            .IsEqualTo("line");
    }

    /// <summary>
    /// D9's case again: clean text, dirty properties. A line the same colour as an untouched file
    /// is a line people stop reading.
    /// </summary>
    [Test]
    public async Task A_property_only_change_is_still_coloured()
    {
        var entry = Entry(NodeStatus.Unmodified) with { PropertyStatus = PropertyStatus.Modified };

        await Assert.That(StatusPalette.Ansi("line", entry)).IsNotEqualTo("line");
    }

    [Test]
    public async Task A_conflict_does_not_look_like_an_ordinary_modification()
    {
        await Assert
            .That(StatusPalette.Ansi("line", Entry(NodeStatus.Conflicted)))
            .IsNotEqualTo(StatusPalette.Ansi("line", Entry(NodeStatus.Modified)));
    }

    private static WorkingCopyEntry Entry(NodeStatus status) =>
        new(
            "art/hero.png",
            NodeKind.File,
            status,
            PropertyStatus.Unmodified,
            Revision: 42,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );
}
