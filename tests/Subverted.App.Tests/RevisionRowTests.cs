using Subverted.App.Presentation;
using Subverted.Core;

namespace Subverted.App.Tests;

public sealed class RevisionRowTests
{
    [Test]
    public async Task The_summary_is_the_first_line_that_says_anything()
    {
        var row = From(message: "\n  \n  Fix the hero's walk cycle  \nSecond line\n");

        await Assert.That(row.Summary).IsEqualTo("Fix the hero's walk cycle");
        await Assert.That(row.Message).IsEqualTo("Fix the hero's walk cycle  \nSecond line");
    }

    [Test]
    public async Task A_windows_line_ending_does_not_reach_the_summary()
    {
        var row = From(message: "First\r\nSecond");

        await Assert.That(row.Summary).IsEqualTo("First");
    }

    [Test]
    [Arguments("")]
    [Arguments(" \n \t ")]
    public async Task A_revision_with_no_message_says_so(string message)
    {
        var row = From(message: message);

        await Assert.That(row.Summary).IsEqualTo(RevisionRow.NoMessage);
        await Assert.That(row.Message).IsEmpty();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("  ")]
    public async Task A_revision_with_no_author_says_so(string? author)
    {
        await Assert.That(From(author: author).Author).IsEqualTo(RevisionRow.NoAuthor);
    }

    [Test]
    public async Task The_author_is_kept_as_svn_recorded_it()
    {
        await Assert.That(From(author: "keiichi").Author).IsEqualTo("keiichi");
    }

    /// <summary>SVN records UTC; the studio reads its own clock.</summary>
    [Test]
    public async Task The_commit_time_is_shown_in_the_viewers_zone()
    {
        var warsaw = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "t", "t");

        var row = RevisionRow.From(Entry(date: Revisions.Committed), warsaw);

        await Assert.That(row.When).IsEqualTo("2026-09-23 15:16");
    }

    [Test]
    public async Task A_revision_with_no_date_shows_none()
    {
        await Assert.That(From(date: null).When).IsEmpty();
    }

    [Test]
    public async Task Changed_paths_keep_the_order_svn_listed_them_in()
    {
        var row = RevisionRow.From(
            new RevisionEntry(
                7,
                "keiichi",
                null,
                "m",
                [
                    new ChangedPath("/z.txt", PathChange.Added, null, null),
                    new ChangedPath("/a.txt", PathChange.Deleted, null, null),
                ]
            ),
            TimeZoneInfo.Utc
        );

        await Assert.That(row.Revision).IsEqualTo(7L);
        await Assert
            .That(row.ChangedPaths.Select(path => path.Path))
            .IsEquivalentTo(
                ["/z.txt", "/a.txt"],
                TUnit.Assertions.Enums.CollectionOrdering.Matching
            );
    }

    private static RevisionRow From(
        string message = "m",
        string? author = "keiichi",
        DateTimeOffset? date = null
    ) => RevisionRow.From(Entry(message, author, date), TimeZoneInfo.Utc);

    private static RevisionEntry Entry(
        string message = "m",
        string? author = "keiichi",
        DateTimeOffset? date = null
    ) => new(1, author, date, message, []);
}
