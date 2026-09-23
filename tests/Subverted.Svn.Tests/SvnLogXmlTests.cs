using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// The parser is pure, so every shape SVN can emit is pinned here rather than needing a server.
/// </summary>
/// <remarks>
/// <see cref="A_real_verbose_log_document_is_read_whole"/> holds a document copied byte for byte
/// out of <c>svn log --xml -v</c> on 1.8.15. Everything else is a variation on it; if the two ever
/// disagree, the real one wins.
/// </remarks>
public sealed class SvnLogXmlTests
{
    private const string RealDocument = """
        <?xml version="1.0" encoding="UTF-8"?>
        <log>
        <logentry
           revision="3">
        <author>aruko</author>
        <date>2026-09-21T05:25:02.540096Z</date>
        <paths>
        <path
           prop-mods="false"
           text-mods="false"
           kind="file"
           action="D">/src/b.txt</path>
        </paths>
        <msg></msg>
        </logentry>
        <logentry
           revision="2">
        <author>aruko</author>
        <date>2026-09-21T05:25:02.064186Z</date>
        <paths>
        <path
           prop-mods="true"
           text-mods="true"
           kind="file"
           action="M">/src/a.txt</path>
        <path
           prop-mods="false"
           text-mods="true"
           kind="file"
           copyfrom-path="/src/a.txt"
           copyfrom-rev="1"
           action="A">/src/b.txt</path>
        </paths>
        <msg>second</msg>
        </logentry>
        <logentry
           revision="1">
        <author>aruko</author>
        <date>2026-09-21T05:25:01.324540Z</date>
        <paths>
        <path
           action="A"
           prop-mods="false"
           text-mods="false"
           kind="dir">/src</path>
        </paths>
        <msg>first commit
        with a second line</msg>
        </logentry>
        </log>
        """;

    [Test]
    public async Task A_real_verbose_log_document_is_read_whole()
    {
        var revisions = SvnLogXml.Parse(RealDocument);

        await Assert.That(revisions.Count).IsEqualTo(3);
        await Assert
            .That(revisions.Select(revision => revision.Revision))
            .IsEquivalentTo([3L, 2L, 1L]);
        await Assert.That(revisions[0].Author).IsEqualTo("aruko");
        await Assert.That(revisions[0].Message).IsEqualTo(string.Empty);
        await Assert
            .That(revisions[0].Date)
            .IsEqualTo(new DateTimeOffset(2026, 9, 21, 5, 25, 2, TimeSpan.Zero).AddTicks(5400960));
        await Assert.That(revisions[2].Message).IsEqualTo("first commit\nwith a second line");
    }

    /// <summary>Newest first is the order SVN answers in, and the order a reader expects.</summary>
    [Test]
    public async Task Revisions_keep_the_order_svn_listed_them_in()
    {
        var revisions = SvnLogXml.Parse(RealDocument);

        await Assert.That(revisions[0].Revision).IsEqualTo(3L);
        await Assert.That(revisions[1].Revision).IsEqualTo(2L);
        await Assert.That(revisions[2].Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task A_deleted_path_carries_its_action_and_no_copy_source()
    {
        var deleted = SvnLogXml.Parse(RealDocument)[0].ChangedPaths.Single();

        await Assert.That(deleted.Path).IsEqualTo("/src/b.txt");
        await Assert.That(deleted.Change).IsEqualTo(PathChange.Deleted);
        await Assert.That(deleted.CopiedFromPath).IsNull();
        await Assert.That(deleted.CopiedFromRevision).IsNull();
    }

    [Test]
    public async Task A_copy_carries_where_it_came_from_and_at_which_revision()
    {
        var copied = SvnLogXml.Parse(RealDocument)[1].ChangedPaths[1];

        await Assert.That(copied.Path).IsEqualTo("/src/b.txt");
        await Assert.That(copied.Change).IsEqualTo(PathChange.Added);
        await Assert.That(copied.CopiedFromPath).IsEqualTo("/src/a.txt");
        await Assert.That(copied.CopiedFromRevision).IsEqualTo(1L);
    }

    [Test]
    public async Task A_log_with_no_revisions_in_it_is_no_revisions_and_not_a_failure()
    {
        await Assert.That(SvnLogXml.Parse("<log></log>")).IsEmpty();
    }

    [Test]
    [Arguments("A", PathChange.Added)]
    [Arguments("D", PathChange.Deleted)]
    [Arguments("M", PathChange.Modified)]
    [Arguments("R", PathChange.Replaced)]
    public async Task Every_action_svn_writes_has_a_change_of_its_own(
        string action,
        PathChange expected
    )
    {
        var revisions = SvnLogXml.Parse(WithPath($"""action="{action}" """));

        await Assert.That(revisions[0].ChangedPaths[0].Change).IsEqualTo(expected);
    }

    /// <summary>
    /// A letter we do not know means SVN grew a concept we have not modelled. Guessing at the
    /// nearest one would print a delete as a modify.
    /// </summary>
    [Test]
    [Arguments("X")]
    [Arguments("")]
    [Arguments("a")]
    public async Task An_action_this_build_does_not_know_fails_the_whole_read(string action)
    {
        await Assert
            .That(() => SvnLogXml.Parse(WithPath($"""action="{action}" """)))
            .Throws<SvnCommandException>()
            .WithMessageContaining(action.Length == 0 ? "''" : $"'{action}'");
    }

    [Test]
    public async Task A_path_with_no_action_at_all_is_refused_rather_than_defaulted()
    {
        await Assert
            .That(() => SvnLogXml.Parse(WithPath(string.Empty)))
            .Throws<SvnCommandException>();
    }

    [Test]
    public async Task A_revision_with_no_paths_element_has_no_changed_paths()
    {
        var revisions = SvnLogXml.Parse(
            """<log><logentry revision="7"><msg>x</msg></logentry></log>"""
        );

        await Assert.That(revisions[0].ChangedPaths).IsEmpty();
        await Assert.That(revisions[0].Revision).IsEqualTo(7L);
    }

    /// <summary>An empty element and a missing one are the same commit: one made with no message.</summary>
    [Test]
    [Arguments("<msg></msg>")]
    [Arguments("")]
    public async Task A_commit_with_no_message_reads_as_empty_and_never_as_null(string element)
    {
        var revisions = SvnLogXml.Parse(
            $"""<log><logentry revision="1">{element}</logentry></log>"""
        );

        await Assert.That(revisions[0].Message).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task An_anonymous_commit_has_no_author_rather_than_an_empty_one()
    {
        var revisions = SvnLogXml.Parse("""<log><logentry revision="1"></logentry></log>""");

        await Assert.That(revisions[0].Author).IsNull();
    }

    [Test]
    public async Task An_author_that_is_present_is_read_as_written()
    {
        var revisions = SvnLogXml.Parse(
            """<log><logentry revision="1"><author>ana</author></logentry></log>"""
        );

        await Assert.That(revisions[0].Author).IsEqualTo("ana");
    }

    /// <summary>
    /// The date is the one field worth losing on its own: svn:date is a revision property and can
    /// be deleted, and a revision number with a message still answers what was asked.
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("not-a-date")]
    public async Task A_date_that_cannot_be_read_costs_the_date_and_not_the_revision(string date)
    {
        var revisions = SvnLogXml.Parse(
            $"""<log><logentry revision="9"><date>{date}</date><msg>m</msg></logentry></log>"""
        );

        await Assert.That(revisions[0].Date).IsNull();
        await Assert.That(revisions[0].Revision).IsEqualTo(9L);
        await Assert.That(revisions[0].Message).IsEqualTo("m");
    }

    [Test]
    public async Task A_missing_date_element_is_the_same_as_an_unreadable_one()
    {
        var revisions = SvnLogXml.Parse("""<log><logentry revision="9"></logentry></log>""");

        await Assert.That(revisions[0].Date).IsNull();
    }

    [Test]
    [Arguments("not xml at all")]
    [Arguments("<log>")]
    [Arguments("")]
    public async Task Text_that_is_not_a_document_is_refused(string text)
    {
        await Assert.That(() => SvnLogXml.Parse(text)).Throws<SvnCommandException>();
    }

    /// <summary>
    /// <c>svn</c> writing a well-formed document that is not a log means we ran the wrong command
    /// or read the wrong pipe, and either way the answer is not a history.
    /// </summary>
    [Test]
    public async Task A_document_that_is_not_a_log_is_refused_by_name()
    {
        await Assert
            .That(() => SvnLogXml.Parse("<status></status>"))
            .Throws<SvnCommandException>()
            .WithMessageContaining("status");
    }

    [Test]
    [Arguments("""<log><logentry revision="x"></logentry></log>""")]
    [Arguments("""<log><logentry></logentry></log>""")]
    public async Task A_revision_that_is_not_a_number_is_refused(string document)
    {
        await Assert
            .That(() => SvnLogXml.Parse(document))
            .Throws<SvnCommandException>()
            .WithMessageContaining("logentry/@revision");
    }

    [Test]
    public async Task A_copy_source_revision_that_is_not_a_number_is_refused()
    {
        await Assert
            .That(() => SvnLogXml.Parse(WithPath("""action="A" copyfrom-rev="soon" """)))
            .Throws<SvnCommandException>()
            .WithMessageContaining("path/@copyfrom-rev");
    }

    /// <summary>A copy source path with no revision beside it still reads; SVN writes both or neither.</summary>
    [Test]
    public async Task A_copy_source_path_without_a_revision_leaves_the_revision_absent()
    {
        var changed = SvnLogXml
            .Parse(WithPath("""action="A" copyfrom-path="/old" """))[0]
            .ChangedPaths[0];

        await Assert.That(changed.CopiedFromPath).IsEqualTo("/old");
        await Assert.That(changed.CopiedFromRevision).IsNull();
    }

    private static string WithPath(string attributes) =>
        $"""<log><logentry revision="1"><paths><path {attributes}>/src/a.txt</path></paths></logentry></log>""";
}
