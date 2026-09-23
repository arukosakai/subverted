using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

public sealed class StatusReportTests
{
    /// <summary>Marks each line so a test can see that painting happened and what it painted.</summary>
    private static readonly Paint Marking = (line, entry) => $"<{entry.Status}>{line}";

    /// <summary>
    /// The daemon appends unversioned nodes after every versioned one, so without this an
    /// unversioned file lands pages away from the versioned file beside it on disk.
    /// </summary>
    [Test]
    public async Task Entries_are_listed_in_path_order_and_not_in_the_order_they_arrived()
    {
        var lines = StatusReport.Compact(
            Response(Entry("b", NodeStatus.Added), Entry("a", NodeStatus.Modified)),
            StatusPalette.Plain
        );

        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines[0]).EndsWith("a");
        await Assert.That(lines[1]).EndsWith("b");
    }

    /// <summary>
    /// Ordinal, the way <c>svn status</c> orders: <c>clean.txt</c> comes immediately before the
    /// <c>clean.txt.prej</c> a property conflict left beside it.
    /// </summary>
    [Test]
    public async Task A_longer_path_sharing_a_prefix_sorts_after_the_shorter_one()
    {
        var lines = StatusReport.Compact(
            Response(
                Entry("clean.txt.prej", NodeStatus.Unversioned),
                Entry("clean.txt", NodeStatus.Conflicted)
            ),
            StatusPalette.Plain
        );

        await Assert.That(lines[0]).EndsWith("clean.txt");
        await Assert.That(lines[1]).EndsWith("clean.txt.prej");
    }

    [Test]
    public async Task Nothing_to_report_is_no_lines_at_all_rather_than_a_reassuring_one()
    {
        await Assert.That(StatusReport.Compact(Response(), StatusPalette.Plain)).IsEmpty();
    }

    [Test]
    public async Task Every_line_goes_through_the_paint_it_was_given()
    {
        var lines = StatusReport.Compact(Response(Entry("a", NodeStatus.Modified)), Marking);

        await Assert.That(lines[0]).StartsWith("<Modified>");
    }

    [Test]
    public async Task Verbose_uses_the_verbose_layout()
    {
        var lines = StatusReport.Verbose(
            Response(Entry("a", NodeStatus.Modified)),
            StatusPalette.Plain
        );

        await Assert.That(lines[0]).IsEqualTo(StatusLine.Verbose(Entry("a", NodeStatus.Modified)));
    }

    [Test]
    public async Task Changelisted_nodes_come_after_the_loose_ones_under_a_heading()
    {
        var lines = StatusReport.Compact(
            Response(
                Entry("staged", NodeStatus.Modified) with
                {
                    Changelist = "assets",
                },
                Entry("loose", NodeStatus.Modified)
            ),
            StatusPalette.Plain
        );

        await Assert.That(lines.Count).IsEqualTo(4);
        await Assert.That(lines[0]).EndsWith("loose");
        await Assert.That(lines[1]).IsEmpty();
        await Assert.That(lines[2]).IsEqualTo("--- Changelist 'assets':");
        await Assert.That(lines[3]).EndsWith("staged");
    }

    [Test]
    public async Task Several_changelists_come_out_in_name_order()
    {
        var lines = StatusReport.Compact(
            Response(
                Entry("z", NodeStatus.Modified) with
                {
                    Changelist = "zebra",
                },
                Entry("a", NodeStatus.Modified) with
                {
                    Changelist = "alpha",
                }
            ),
            StatusPalette.Plain
        );

        await Assert.That(lines.Count).IsEqualTo(6);
        await Assert.That(lines[1]).IsEqualTo("--- Changelist 'alpha':");
        await Assert.That(lines[2]).EndsWith("a");
        await Assert.That(lines[4]).IsEqualTo("--- Changelist 'zebra':");
        await Assert.That(lines[5]).EndsWith("z");
    }

    /// <summary>
    /// The footer exists because <c>*</c> means nothing to anyone on its own. It must not appear
    /// when there is no <c>*</c> in the listing to explain.
    /// </summary>
    [Test]
    public async Task The_undecided_footer_appears_only_when_something_is_undecided()
    {
        var withUndecided = StatusReport.Compact(
            Response(Entry("a", NodeStatus.NeedsPristineCompare)),
            StatusPalette.Plain
        );
        var without = StatusReport.Compact(
            Response(Entry("a", NodeStatus.Modified)),
            StatusPalette.Plain
        );

        await Assert.That(withUndecided[^1]).Contains("svn status");
        await Assert.That(without.Count).IsEqualTo(1);
    }

    /// <summary>
    /// An obstruction gets its own footer, and the two are independent: a listing with one must
    /// not explain the other, or the explanation stops being worth reading.
    /// </summary>
    [Test]
    public async Task The_obstruction_footer_appears_only_when_something_is_obstructed()
    {
        var obstructed = StatusReport.Compact(
            Response(Entry("a", NodeStatus.Obstructed)),
            StatusPalette.Plain
        );
        var undecided = StatusReport.Compact(
            Response(Entry("a", NodeStatus.NeedsPristineCompare)),
            StatusPalette.Plain
        );

        await Assert.That(obstructed[^1]).Contains("versioned as one kind");
        await Assert.That(obstructed.Any(line => line.Contains("svn status"))).IsFalse();
        await Assert.That(undecided.Any(line => line.Contains("versioned as one kind"))).IsFalse();
    }

    /// <summary>
    /// Both at once: each footer is appended on its own, so a listing carrying both states has to
    /// explain both rather than whichever was checked first.
    /// </summary>
    [Test]
    public async Task A_listing_with_both_states_explains_both()
    {
        var lines = StatusReport.Compact(
            Response(
                Entry("a", NodeStatus.NeedsPristineCompare),
                Entry("b", NodeStatus.Obstructed)
            ),
            StatusPalette.Plain
        );

        await Assert.That(lines.Any(line => line.Contains("svn status"))).IsTrue();
        await Assert.That(lines.Any(line => line.Contains("versioned as one kind"))).IsTrue();
    }

    /// <summary>
    /// The state in which <c>svn status</c> refuses to answer at all. Subverted answers anyway, so
    /// the listing has to say whose reading it is.
    /// </summary>
    [Test]
    public async Task A_working_copy_svn_will_not_read_is_warned_about()
    {
        var lines = StatusReport.Compact(
            Wedged(2, Entry("a", NodeStatus.Modified)),
            StatusPalette.Plain
        );

        await Assert.That(lines[0]).StartsWith("warning: 2 interrupted operation(s)");
        await Assert.That(lines[0]).Contains("sv cleanup");
    }

    [Test]
    public async Task A_working_copy_with_nothing_queued_is_not_warned_about()
    {
        var lines = StatusReport.Compact(
            Response(Entry("a", NodeStatus.Modified)),
            StatusPalette.Plain
        );

        await Assert.That(lines.Any(line => line.Contains("warning:"))).IsFalse();
    }

    /// <summary>
    /// Ahead of the listing rather than after it, because it does not explain one line — it says
    /// every line below it is Subverted's own reading. A reader who has scrolled past has missed it.
    /// </summary>
    [Test]
    public async Task The_warning_comes_before_the_listing_it_qualifies()
    {
        var lines = StatusReport.Compact(
            Wedged(1, Entry("a", NodeStatus.Modified)),
            StatusPalette.Plain
        );

        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That(lines[1]).IsEqualTo(string.Empty);
        await Assert.That(lines[2]).EndsWith("a");
    }

    /// <summary>
    /// A wedged working copy with nothing modified in it still has to say so — the listing being
    /// empty is exactly the reassuring answer that would be wrong.
    /// </summary>
    [Test]
    public async Task A_wedged_working_copy_with_nothing_to_list_still_warns()
    {
        var lines = StatusReport.Compact(Wedged(1), StatusPalette.Plain);

        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines[0]).StartsWith("warning: 1 interrupted operation(s)");
    }

    [Test]
    public async Task The_verbose_listing_carries_the_warning_too()
    {
        var lines = StatusReport.Verbose(
            Wedged(1, Entry("a", NodeStatus.Modified)),
            StatusPalette.Plain
        );

        await Assert.That(lines[0]).StartsWith("warning: 1 interrupted operation(s)");
    }

    /// <summary>
    /// After the listing, unlike the wedged-copy warning: what this explains is the pair of lines
    /// above it, and a reader who has just seen them is the one it is for.
    /// </summary>
    [Test]
    public async Task A_rename_made_outside_svn_is_named_under_the_listing()
    {
        var lines = StatusReport.Compact(
            Renamed(
                [new UnrecordedMove("art/hero.png", "art/protagonist.png")],
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("art/protagonist.png", NodeStatus.Unversioned)
            ),
            StatusPalette.Plain
        );

        await Assert.That(lines[0]).EndsWith("art/hero.png");
        await Assert.That(lines[1]).EndsWith("art/protagonist.png");
        await Assert.That(lines[2]).IsEmpty();
        await Assert.That(lines[3]).Contains("1 file(s) above look renamed outside SVN");
        await Assert.That(lines[3]).Contains("sv mv OLD NEW");
        await Assert.That(lines[4]).IsEqualTo("  art/hero.png -> art/protagonist.png");
    }

    [Test]
    public async Task Every_paired_rename_is_listed()
    {
        var lines = StatusReport.Compact(
            Renamed([
                new UnrecordedMove("a.png", "one.png"),
                new UnrecordedMove("b.png", "two.png"),
            ]),
            StatusPalette.Plain
        );

        await Assert.That(lines[^2]).IsEqualTo("  a.png -> one.png");
        await Assert.That(lines[^1]).IsEqualTo("  b.png -> two.png");
    }

    /// <summary>
    /// The negative side: a listing with no pairs must not print the heading, or every clean status
    /// carries advice about a problem nobody has.
    /// </summary>
    [Test]
    public async Task A_listing_with_no_pairs_says_nothing_about_renames()
    {
        var lines = StatusReport.Compact(
            Response(Entry("art/hero.png", NodeStatus.Modified)),
            StatusPalette.Plain
        );

        await Assert.That(lines.Any(line => line.Contains("renamed outside SVN"))).IsFalse();
    }

    [Test]
    public async Task The_verbose_listing_names_renames_too()
    {
        var lines = StatusReport.Verbose(
            Renamed([new UnrecordedMove("art/hero.png", "art/protagonist.png")]),
            StatusPalette.Plain
        );

        await Assert.That(lines[^1]).IsEqualTo("  art/hero.png -> art/protagonist.png");
    }

    private static StatusResponse Response(params WorkingCopyEntry[] entries) =>
        new(
            new WorkingCopyInfo("/wc", "https://svn.example/repo", "uuid-1", 31),
            entries,
            ServedFromWarmIndex: true,
            ServerElapsedMilliseconds: 0.4,
            UnfinishedOperations: 0,
            UnrecordedMoves: []
        );

    private static StatusResponse Renamed(
        IReadOnlyList<UnrecordedMove> moves,
        params WorkingCopyEntry[] entries
    ) => Response(entries) with { UnrecordedMoves = moves };

    private static StatusResponse Wedged(int operations, params WorkingCopyEntry[] entries) =>
        Response(entries) with
        {
            UnfinishedOperations = operations,
        };

    private static WorkingCopyEntry Entry(string relPath, NodeStatus status) =>
        new(
            relPath,
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
