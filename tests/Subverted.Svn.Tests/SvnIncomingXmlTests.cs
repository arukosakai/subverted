using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// The server's half of <c>svn status -u --xml</c>, read without a server.
/// </summary>
/// <remarks>
/// <see cref="RealDocument"/> is <c>svn status -u --xml --ignore-externals .</c> on 1.8.15, run in
/// the root of a copy at r1 while a teammate committed r2–r6: an edit to <c>sub/f.txt</c>, a
/// property on <c>quiet</c>, a new <c>newdir</c> with two files, <c>quiet</c> deleted and
/// <c>sub/f.txt</c> replaced. <c>root.txt</c> is edited here and nowhere else. Only the author was
/// renamed.
/// </remarks>
public sealed class SvnIncomingXmlTests
{
    private const string RealDocument = """
        <?xml version="1.0" encoding="UTF-8"?>
        <status>
        <target
           path=".">
        <entry
           path="newdir\1.txt">
        <wc-status
           props="none"
           item="none">
        </wc-status>
        <repos-status
           props="none"
           item="added">
        </repos-status>
        </entry>
        <entry
           path="newdir\2.txt">
        <wc-status
           props="none"
           item="none">
        </wc-status>
        <repos-status
           props="none"
           item="added">
        </repos-status>
        </entry>
        <entry
           path="newdir">
        <wc-status
           item="none"
           props="none">
        </wc-status>
        <repos-status
           item="added"
           props="none">
        </repos-status>
        </entry>
        <entry
           path="sub\f.txt">
        <wc-status
           props="none"
           item="normal"
           revision="1">
        <commit
           revision="1">
        <author>rika</author>
        <date>2026-09-28T09:53:39.315593Z</date>
        </commit>
        </wc-status>
        <repos-status
           props="none"
           item="replaced">
        </repos-status>
        </entry>
        <entry
           path="sub">
        <wc-status
           item="normal"
           revision="1"
           props="none">
        <commit
           revision="1">
        <author>rika</author>
        <date>2026-09-28T09:53:39.315593Z</date>
        </commit>
        </wc-status>
        <repos-status
           item="modified"
           props="none">
        </repos-status>
        </entry>
        <entry
           path="root.txt">
        <wc-status
           props="none"
           item="modified"
           revision="1">
        <commit
           revision="1">
        <author>rika</author>
        <date>2026-09-28T09:53:39.315593Z</date>
        </commit>
        </wc-status>
        </entry>
        <entry
           path="quiet">
        <wc-status
           props="none"
           item="normal"
           revision="1">
        <commit
           revision="1">
        <author>rika</author>
        <date>2026-09-28T09:53:39.315593Z</date>
        </commit>
        </wc-status>
        <repos-status
           props="none"
           item="deleted">
        </repos-status>
        </entry>
        <entry
           path=".">
        <wc-status
           item="normal"
           revision="1"
           props="none">
        <commit
           revision="1">
        <author>rika</author>
        <date>2026-09-28T09:53:39.315593Z</date>
        </commit>
        </wc-status>
        <repos-status
           props="none"
           item="modified">
        </repos-status>
        </entry>
        <against
           revision="6"/>
        </target>
        </status>
        """;

    [Test]
    public async Task A_real_document_lists_every_marked_path_and_nothing_changed_only_here()
    {
        var incoming = SvnIncomingXml.Parse(RealDocument, '\\');

        await Assert.That(incoming.AgainstRevision).IsEqualTo(6L);
        await Assert
            .That(incoming.Changes)
            .IsEquivalentTo([
                new IncomingChange("newdir/1.txt", PathChange.Added, false),
                new IncomingChange("newdir/2.txt", PathChange.Added, false),
                new IncomingChange("newdir", PathChange.Added, false),
                new IncomingChange("sub/f.txt", PathChange.Replaced, false),
                new IncomingChange("sub", PathChange.Modified, false),
                new IncomingChange("quiet", PathChange.Deleted, false),
                new IncomingChange("", PathChange.Modified, false),
            ]);
    }

    /// <summary>Captured on 1.8.15 after a teammate set a property on the folder <c>quiet</c>.</summary>
    [Test]
    public async Task A_change_to_properties_alone_has_no_node_change()
    {
        var incoming = SvnIncomingXml.Parse(Document(Entry("quiet", "none", "modified")), '\\');

        await Assert
            .That(incoming.Changes)
            .IsEquivalentTo([new IncomingChange("quiet", null, PropertiesChanged: true)]);
    }

    [Test]
    public async Task A_node_and_its_properties_changed_together_carry_both()
    {
        var incoming = SvnIncomingXml.Parse(Document(Entry("a.txt", "modified", "modified")), '\\');

        await Assert
            .That(incoming.Changes)
            .IsEquivalentTo([new IncomingChange("a.txt", PathChange.Modified, true)]);
    }

    [Test]
    public async Task A_repos_status_of_none_on_both_axes_is_not_incoming()
    {
        var incoming = SvnIncomingXml.Parse(Document(Entry("a.txt", "none", "none")), '\\');

        await Assert.That(incoming.Changes).IsEmpty();
    }

    [Test]
    [Arguments("added", PathChange.Added)]
    [Arguments("deleted", PathChange.Deleted)]
    [Arguments("modified", PathChange.Modified)]
    [Arguments("replaced", PathChange.Replaced)]
    public async Task Each_node_state_reads_as_its_change(string item, PathChange expected)
    {
        var incoming = SvnIncomingXml.Parse(Document(Entry("a.txt", item, "none")), '\\');

        await Assert.That(incoming.Changes.Single().Change).IsEqualTo(expected);
    }

    [Test]
    public async Task Paths_keep_the_separator_they_were_written_with_on_a_unix_client()
    {
        var incoming = SvnIncomingXml.Parse(Document(Entry("art/a\\b.png", "added", "none")), '/');

        await Assert.That(incoming.Changes.Single().RelPath).IsEqualTo("art/a\\b.png");
    }

    /// <summary>
    /// <c>normal</c> is a working-copy state, and 1.8.15 was never seen to write it on the server's
    /// side. Reading it as "nothing incoming" would be a guess, so it fails like any other.
    /// </summary>
    [Test]
    [Arguments("normal", "none")]
    [Arguments("conflicted", "none")]
    [Arguments("none", "normal")]
    [Arguments("none", "conflicted")]
    public async Task A_state_this_build_does_not_model_fails_the_whole_answer(
        string item,
        string props
    )
    {
        await Assert
            .That(() => SvnIncomingXml.Parse(Document(Entry("a.txt", item, props)), '\\'))
            .Throws<SvnCommandException>();
    }

    [Test]
    public async Task A_document_that_never_asked_the_server_is_refused()
    {
        const string local = """
            <status><target path="."><entry path="a.txt"><wc-status item="modified" props="none"/></entry></target></status>
            """;

        await Assert.That(() => SvnIncomingXml.Parse(local, '\\')).Throws<SvnCommandException>();
    }

    [Test]
    public async Task An_against_revision_that_is_not_a_number_is_refused()
    {
        const string broken = """
            <status><target path="."><against revision="HEAD"/></target></status>
            """;

        await Assert.That(() => SvnIncomingXml.Parse(broken, '\\')).Throws<SvnCommandException>();
    }

    [Test]
    public async Task Text_that_is_not_xml_is_refused()
    {
        await Assert
            .That(() => SvnIncomingXml.Parse("svn: E170013: Unable to connect", '\\'))
            .Throws<SvnCommandException>();
    }

    [Test]
    public async Task A_document_that_is_not_a_status_is_refused()
    {
        await Assert.That(() => SvnIncomingXml.Parse("<log/>", '\\')).Throws<SvnCommandException>();
    }

    [Test]
    public async Task A_repos_status_missing_an_axis_is_refused()
    {
        const string broken = """
            <status><target path="."><entry path="a.txt"><repos-status item="added"/></entry><against revision="2"/></target></status>
            """;

        await Assert.That(() => SvnIncomingXml.Parse(broken, '\\')).Throws<SvnCommandException>();
    }

    private static string Entry(string path, string item, string props) =>
        $"""<entry path="{path}"><wc-status item="normal" props="none"/><repos-status item="{item}" props="{props}"/></entry>""";

    private static string Document(string entries) =>
        $"""<status><target path=".">{entries}<against revision="4"/></target></status>""";
}
