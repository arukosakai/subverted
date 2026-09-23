using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

public sealed class LogReportTests
{
    /// <summary>Fixed, so the dates asserted below do not depend on where the build machine is.</summary>
    private static readonly TimeZoneInfo TwoHoursAhead = TimeZoneInfo.CreateCustomTimeZone(
        "fixture",
        TimeSpan.FromHours(2),
        "fixture",
        "fixture"
    );

    private static readonly DateTimeOffset Committed = new(2026, 9, 19, 10, 30, 0, TimeSpan.Zero);

    [Test]
    public async Task An_empty_history_says_so_rather_than_printing_nothing()
    {
        var lines = Lines(new LogResponse([]));

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0]).IsEqualTo("no revisions");
    }

    [Test]
    public async Task A_revision_leads_with_its_number_author_and_date()
    {
        var lines = Lines(new LogResponse([Revision(42)]));

        await Assert.That(lines[0]).IsEqualTo("r42  artist  2026-09-19 12:30");
    }

    /// <summary>
    /// The zone is applied, not ignored: 10:30 UTC is 12:30 where the studio is, and a history
    /// that reads two hours off is one people stop trusting.
    /// </summary>
    [Test]
    public async Task The_date_is_shown_in_the_zone_it_was_given()
    {
        var lines = LogReport.Lines(
            new LogResponse([Revision(42)]),
            limit: null,
            TimeZoneInfo.Utc,
            LogPalette.Plain
        );

        await Assert.That(lines[0]).IsEqualTo("r42  artist  2026-09-19 10:30");
    }

    [Test]
    public async Task A_revision_with_no_author_or_date_says_which_is_missing()
    {
        var lines = Lines(new LogResponse([Revision(42) with { Author = null, Date = null }]));

        await Assert.That(lines[0]).IsEqualTo("r42  (no author)  (no date)");
    }

    [Test]
    public async Task The_message_is_indented_under_its_revision()
    {
        var lines = Lines(new LogResponse([Revision(42) with { Message = "re-export hero" }]));

        await Assert.That(lines[^1]).IsEqualTo("    re-export hero");
    }

    [Test]
    public async Task A_multi_line_message_keeps_its_lines()
    {
        var lines = Lines(new LogResponse([Revision(42) with { Message = "first\nsecond" }]));

        await Assert.That(lines[1]).IsEqualTo("    first");
        await Assert.That(lines[2]).IsEqualTo("    second");
    }

    /// <summary>
    /// SVN accepts a commit with no message, and blank lines around one are SVN's, not the
    /// author's. Printing them puts gaps in the listing that mean nothing.
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("\n")]
    [Arguments("\r\n\r\n")]
    public async Task A_message_that_is_only_blank_lines_prints_nothing(string message)
    {
        var lines = Lines(new LogResponse([Revision(42) with { Message = message }]));

        await Assert.That(lines.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_carriage_return_does_not_survive_into_the_output()
    {
        var lines = Lines(
            new LogResponse([Revision(42) with { Message = "windows\r\nwrote this" }])
        );

        await Assert.That(lines[1]).IsEqualTo("    windows");
        await Assert.That(lines[2]).IsEqualTo("    wrote this");
    }

    [Test]
    [Arguments(PathChange.Added, 'A')]
    [Arguments(PathChange.Deleted, 'D')]
    [Arguments(PathChange.Modified, 'M')]
    [Arguments(PathChange.Replaced, 'R')]
    public async Task Each_kind_of_change_gets_the_letter_svn_prints(
        PathChange change,
        char expected
    )
    {
        var lines = Lines(
            new LogResponse([
                Revision(42) with
                {
                    ChangedPaths = [new ChangedPath("/art/hero.png", change, null, null)],
                },
            ])
        );

        await Assert.That(lines[1]).IsEqualTo($"    {expected} /art/hero.png");
    }

    /// <summary>Where a branch came from is the question people open a log to answer.</summary>
    [Test]
    public async Task A_copy_says_where_it_came_from()
    {
        var lines = Lines(
            new LogResponse([
                Revision(42) with
                {
                    ChangedPaths = [new ChangedPath("/branches/x", PathChange.Added, "/trunk", 41)],
                },
            ])
        );

        await Assert.That(lines[1]).IsEqualTo("    A /branches/x (from /trunk@41)");
    }

    [Test]
    public async Task Revisions_are_separated_by_a_blank_line_and_the_first_one_is_not()
    {
        var lines = Lines(new LogResponse([Revision(43), Revision(42)]));

        await Assert.That(lines[0]).StartsWith("r43");
        await Assert.That(lines[1]).IsEqualTo("    message");
        await Assert.That(lines[2]).IsEmpty();
        await Assert.That(lines[3]).StartsWith("r42");
    }

    /// <summary>
    /// A listing exactly as long as the cap is almost certainly not the whole history. Saying so is
    /// the difference between "that is all of it" and "that is all you asked for".
    /// </summary>
    [Test]
    public async Task A_listing_that_filled_its_limit_says_there_may_be_more()
    {
        var lines = LogReport.Lines(
            new LogResponse([Revision(43), Revision(42)]),
            limit: 2,
            TwoHoursAhead,
            LogPalette.Plain
        );

        await Assert.That(lines[^1]).IsEqualTo("(newest 2; --all for the rest)");
    }

    [Test]
    public async Task A_listing_shorter_than_its_limit_is_the_whole_history_and_says_nothing()
    {
        var lines = LogReport.Lines(
            new LogResponse([Revision(42)]),
            limit: 2,
            TwoHoursAhead,
            LogPalette.Plain
        );

        await Assert.That(lines[^1]).DoesNotContain("--all");
    }

    [Test]
    public async Task An_uncapped_listing_never_says_there_may_be_more()
    {
        var lines = LogReport.Lines(
            new LogResponse([Revision(43), Revision(42)]),
            limit: null,
            TwoHoursAhead,
            LogPalette.Plain
        );

        await Assert.That(lines[^1]).DoesNotContain("--all");
    }

    /// <summary>
    /// Layout decides what each line is and colour reads that, so neither has to recognise the
    /// other's text. This is the test that would catch them drifting apart.
    /// </summary>
    [Test]
    public async Task Each_line_is_painted_as_what_it_actually_is()
    {
        var lines = LogReport.Lines(
            new LogResponse([
                Revision(42) with
                {
                    ChangedPaths = [new ChangedPath("/a", PathChange.Modified, null, null)],
                },
            ]),
            limit: null,
            TwoHoursAhead,
            (line, role) => $"<{role}>{line}"
        );

        await Assert.That(lines[0]).StartsWith("<Heading>");
        await Assert.That(lines[1]).StartsWith("<Path>");
        await Assert.That(lines[2]).StartsWith("<Message>");
    }

    private static IReadOnlyList<string> Lines(LogResponse response) =>
        LogReport.Lines(response, limit: null, TwoHoursAhead, LogPalette.Plain);

    private static RevisionEntry Revision(long revision) =>
        new(revision, "artist", Committed, "message", []);
}
