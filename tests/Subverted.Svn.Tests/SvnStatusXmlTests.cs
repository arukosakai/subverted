using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// The fallback parser is pure, so every shape SVN can emit is pinned here rather than needing a
/// working copy in that shape.
/// </summary>
/// <remarks>
/// <see cref="A_real_verbose_status_document_is_read_node_for_node"/> holds a document copied byte
/// for byte out of <c>svn status --xml -v --no-ignore .</c> on 1.8.15, run in the root so the paths
/// come back relative. Everything else is a variation on it; if the two ever disagree, the real one
/// wins.
/// </remarks>
public sealed class SvnStatusXmlTests
{
    private const string RealDocument = """
        <?xml version="1.0" encoding="UTF-8"?>
        <status>
        <target
           path=".">
        <entry
           path=".">
        <wc-status
           props="none"
           item="normal"
           revision="0">
        <commit
           revision="0">
        <date>2026-09-18T12:54:00.601688Z</date>
        </commit>
        </wc-status>
        </entry>
        <entry
           path="added.txt">
        <wc-status
           props="none"
           item="added"
           revision="-1">
        </wc-status>
        </entry>
        <entry
           path="assets">
        <wc-status
           item="normal"
           revision="1"
           props="none">
        <commit
           revision="1">
        <author>aruko</author>
        <date>2026-09-18T12:54:15.467058Z</date>
        </commit>
        </wc-status>
        </entry>
        <entry
           path="assets\hero.png">
        <wc-status
           props="none"
           item="normal"
           revision="1">
        <commit
           revision="1">
        <author>aruko</author>
        <date>2026-09-18T12:54:15.467058Z</date>
        </commit>
        <lock>
        <token>opaquelocktoken:6c25ead6-22c0-1245-a687-5849b8600389</token>
        <owner>aruko</owner>
        <comment>editing</comment>
        <created>2026-09-18T12:54:52.631001Z</created>
        </lock>
        </wc-status>
        </entry>
        <entry
           path="deleted.txt">
        <wc-status
           props="none"
           item="deleted"
           revision="1">
        </wc-status>
        </entry>
        <entry
           path="missing.txt">
        <wc-status
           props="none"
           item="missing"
           revision="1">
        </wc-status>
        </entry>
        <entry
           path="modified.txt">
        <wc-status
           item="modified"
           revision="1"
           props="none">
        </wc-status>
        </entry>
        <entry
           path="replaced.txt">
        <wc-status
           props="none"
           item="replaced"
           revision="1">
        </wc-status>
        </entry>
        <entry
           path="unversioned.txt">
        <wc-status
           item="unversioned"
           props="none">
        </wc-status>
        </entry>
        </target>
        <changelist
           name="art-pass">
        <entry
           path="changelisted.txt">
        <wc-status
           item="modified"
           revision="1"
           props="none">
        </wc-status>
        </entry>
        </changelist>
        </status>
        """;

    [Test]
    public async Task A_real_verbose_status_document_is_read_node_for_node()
    {
        var entries = SvnStatusXml.Parse(RealDocument, '\\');

        await Assert
            .That(entries.Select(entry => entry.RelPath))
            .IsEquivalentTo([
                "",
                "added.txt",
                "assets",
                "assets/hero.png",
                "deleted.txt",
                "missing.txt",
                "modified.txt",
                "replaced.txt",
                "unversioned.txt",
                "changelisted.txt",
            ]);
        await Assert
            .That(entries.Select(entry => entry.Status))
            .IsEquivalentTo([
                NodeStatus.Unmodified,
                NodeStatus.Added,
                NodeStatus.Unmodified,
                NodeStatus.Unmodified,
                NodeStatus.Deleted,
                NodeStatus.Missing,
                NodeStatus.Modified,
                NodeStatus.Replaced,
                NodeStatus.Unversioned,
                NodeStatus.Modified,
            ]);
    }

    /// <summary>
    /// The root is <c>.</c> on the wire and the empty string in the model, which is what the wc.db
    /// path calls it. Getting this wrong puts a node called "." in every listing.
    /// </summary>
    [Test]
    public async Task The_working_copy_root_comes_back_under_the_empty_relative_path()
    {
        var entries = SvnStatusXml.Parse(RealDocument, '\\');

        await Assert.That(entries[0].RelPath).IsEqualTo(string.Empty);
        await Assert.That(entries[0].Revision).IsEqualTo(0L);
    }

    [Test]
    public async Task A_nested_path_is_rewritten_to_the_separator_the_model_uses()
    {
        var entries = SvnStatusXml.Parse(RealDocument, '\\');

        await Assert.That(Single(entries, "assets/hero.png").RelPath).IsEqualTo("assets/hero.png");
    }

    /// <summary>
    /// On a machine whose separator is already <c>/</c> the path arrives needing nothing done to
    /// it — and a backslash in it is then part of a filename, not a separator.
    /// </summary>
    [Test]
    public async Task A_path_written_with_the_separator_the_model_uses_is_left_alone()
    {
        var entries = SvnStatusXml.Parse(
            Document("""<entry path="a\b/c.txt"><wc-status item="normal" props="none"/></entry>"""),
            '/'
        );

        await Assert.That(entries[0].RelPath).IsEqualTo(@"a\b/c.txt");
    }

    [Test]
    public async Task A_held_lock_is_the_one_thing_the_lock_element_says()
    {
        var entries = SvnStatusXml.Parse(RealDocument, '\\');

        await Assert.That(Single(entries, "assets/hero.png").HasLockToken).IsTrue();
        await Assert.That(Single(entries, "modified.txt").HasLockToken).IsFalse();
    }

    /// <summary>
    /// The fallback has to agree with the fast path about the third column, or which reader answered
    /// becomes visible to the user. SVN writes the attribute only on a locked node, so its absence
    /// is the healthy case rather than a missing field.
    /// </summary>
    [Test]
    public async Task A_working_copy_write_lock_is_the_wc_locked_attribute()
    {
        var entries = SvnStatusXml.Parse(
            Document(
                """
                <entry path="sub"><wc-status item="normal" props="none" wc-locked="true"/></entry>
                <entry path="other"><wc-status item="normal" props="none"/></entry>
                """
            ),
            '/'
        );

        await Assert.That(Single(entries, "sub").IsWriteLocked).IsTrue();
        await Assert.That(Single(entries, "other").IsWriteLocked).IsFalse();
    }

    /// <summary>
    /// Anything but <c>"true"</c> is not a lock. Reading the attribute's presence alone would make
    /// <c>wc-locked="false"</c> — which SVN does not write, but which costs nothing to be right
    /// about — report every node as wedged.
    /// </summary>
    [Test]
    public async Task A_wc_locked_attribute_that_is_not_true_is_not_a_lock()
    {
        var entries = SvnStatusXml.Parse(
            Document(
                """<entry path="sub"><wc-status item="normal" props="none" wc-locked="false"/></entry>"""
            ),
            '/'
        );

        await Assert.That(entries[0].IsWriteLocked).IsFalse();
    }

    /// <summary>
    /// Shaped on SVN 1.8.15's own output for <c>file-copy.txt</c> and <c>plain.txt</c>: a copy
    /// carries <c>copied="true"</c>, and a plain add carries no attribute at all.
    /// </summary>
    [Test]
    public async Task A_copied_attribute_marks_a_node_scheduled_with_history()
    {
        var entries = SvnStatusXml.Parse(
            Document(
                """
                <entry path="file-copy.txt"><wc-status item="added" props="none" copied="true"/></entry>
                <entry path="plain.txt"><wc-status item="added" revision="-1" props="none"/></entry>
                """
            ),
            '/'
        );

        await Assert.That(Single(entries, "file-copy.txt").IsCopied).IsTrue();
        await Assert.That(Single(entries, "plain.txt").IsCopied).IsFalse();
    }

    [Test]
    public async Task A_copied_attribute_that_is_not_true_is_not_a_copy()
    {
        var entries = SvnStatusXml.Parse(
            Document(
                """<entry path="a.txt"><wc-status item="added" props="none" copied="false"/></entry>"""
            ),
            '/'
        );

        await Assert.That(entries[0].IsCopied).IsFalse();
    }

    [Test]
    public async Task A_changelisted_node_carries_the_name_of_the_element_it_sits_in()
    {
        var entries = SvnStatusXml.Parse(RealDocument, '\\');

        await Assert.That(Single(entries, "changelisted.txt").Changelist).IsEqualTo("art-pass");
        await Assert.That(Single(entries, "modified.txt").Changelist).IsNull();
    }

    /// <summary>
    /// SVN writes <c>-1</c> for a local add and leaves the attribute off an unversioned node. Both
    /// mean the same thing, and reading <c>-1</c> as a revision would print it as one.
    /// </summary>
    [Test]
    [Arguments("added.txt")]
    [Arguments("unversioned.txt")]
    public async Task A_node_with_no_base_revision_comes_back_without_one(string relPath)
    {
        var entries = SvnStatusXml.Parse(RealDocument, '\\');

        await Assert.That(Single(entries, relPath).Revision).IsNull();
    }

    [Test]
    public async Task A_node_with_a_base_revision_keeps_it()
    {
        var entries = SvnStatusXml.Parse(RealDocument, '\\');

        await Assert.That(Single(entries, "modified.txt").Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments("normal", NodeStatus.Unmodified)]
    [Arguments("modified", NodeStatus.Modified)]
    [Arguments("added", NodeStatus.Added)]
    [Arguments("deleted", NodeStatus.Deleted)]
    [Arguments("replaced", NodeStatus.Replaced)]
    [Arguments("missing", NodeStatus.Missing)]
    [Arguments("incomplete", NodeStatus.Incomplete)]
    [Arguments("conflicted", NodeStatus.Conflicted)]
    [Arguments("unversioned", NodeStatus.Unversioned)]
    [Arguments("ignored", NodeStatus.Ignored)]
    [Arguments("obstructed", NodeStatus.Obstructed)]
    [Arguments("external", NodeStatus.External)]
    public async Task Every_node_state_svn_reports_maps_to_the_one_it_means(
        string item,
        NodeStatus expected
    )
    {
        var entries = SvnStatusXml.Parse(Entry(item: item), '/');

        await Assert.That(entries[0].Status).IsEqualTo(expected);
    }

    /// <summary>
    /// A state with no Subverted equivalent still fails the whole read rather than being called
    /// "normal" — the same rule <see cref="SvnLogXml"/> follows for an action it cannot read.
    /// Obstructed and external used to be on this list and are now modelled; <c>merged</c> is what
    /// is left, so the rule still has something to hold.
    /// </summary>
    [Test]
    [Arguments("merged")]
    [Arguments("some-state-svn-has-not-invented-yet")]
    public async Task A_node_state_this_build_does_not_model_fails_the_read(string item)
    {
        await Assert
            .That(() => SvnStatusXml.Parse(Entry(item: item), '/'))
            .Throws<SvnCommandException>()
            .WithMessageContaining(item);
    }

    /// <summary>
    /// <c>svn status --xml</c> reports an external twice: the placeholder in this working copy,
    /// then the external's own checkout, contents and all. Keeping both gives one path two
    /// statuses and imports another working copy's nodes into this listing.
    /// </summary>
    [Test]
    public async Task An_externals_own_contents_are_dropped_and_only_the_placeholder_survives()
    {
        var document = Document(
            """
            <entry path="."><wc-status item="normal" props="none" revision="2"/></entry>
            <entry path="ext"><wc-status item="external" props="none"/></entry>
            <entry path="keep.txt"><wc-status item="modified" props="none" revision="2"/></entry>
            <entry path="ext"><wc-status item="normal" props="none" revision="2"/></entry>
            <entry path="ext/inner.txt"><wc-status item="modified" props="none" revision="2"/></entry>
            """
        );

        var entries = SvnStatusXml.Parse(document, '/');

        await Assert
            .That(entries.Select(entry => entry.RelPath).Order(StringComparer.Ordinal))
            .IsEquivalentTo(new[] { string.Empty, "ext", "keep.txt" });
        await Assert.That(Single(entries, "ext").Status).IsEqualTo(NodeStatus.External);
        await Assert.That(Single(entries, "ext").Revision).IsNull();
        // A sibling whose name merely starts with the external's is not inside it.
        await Assert.That(Single(entries, "keep.txt").Status).IsEqualTo(NodeStatus.Modified);
    }

    /// <summary>
    /// The filter must key on the path boundary, not on a string prefix: <c>extra.txt</c> is not
    /// inside an external called <c>ext</c>.
    /// </summary>
    [Test]
    public async Task A_sibling_sharing_the_externals_name_as_a_prefix_is_kept()
    {
        var document = Document(
            """
            <entry path="ext"><wc-status item="external" props="none"/></entry>
            <entry path="extra.txt"><wc-status item="modified" props="none" revision="2"/></entry>
            """
        );

        var entries = SvnStatusXml.Parse(document, '/');

        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(Single(entries, "extra.txt").Status).IsEqualTo(NodeStatus.Modified);
    }

    [Test]
    [Arguments("none", PropertyStatus.Unmodified)]
    [Arguments("normal", PropertyStatus.Unmodified)]
    [Arguments("modified", PropertyStatus.Modified)]
    [Arguments("conflicted", PropertyStatus.Modified)]
    public async Task Every_property_state_svn_reports_maps_to_the_one_it_means(
        string props,
        PropertyStatus expected
    )
    {
        var entries = SvnStatusXml.Parse(Entry(props: props), '/');

        await Assert.That(entries[0].PropertyStatus).IsEqualTo(expected);
    }

    /// <summary>
    /// The one place SVN contradicts itself: a copy whose properties differ from its source comes
    /// back <c>props="modified"</c> in the XML while <c>svn status</c> prints nothing in its second
    /// column, because the add is what carries the properties. The wc.db path answers the same from
    /// op_depth, so the fallback follows the column and not the attribute.
    /// </summary>
    [Test]
    [Arguments("added")]
    [Arguments("replaced")]
    [Arguments("deleted")]
    public async Task A_node_with_a_local_tree_layer_reports_no_property_change(string item)
    {
        var entries = SvnStatusXml.Parse(Entry(item: item, props: "modified"), '/');

        await Assert.That(entries[0].PropertyStatus).IsEqualTo(PropertyStatus.Unmodified);
    }

    [Test]
    [Arguments("normal")]
    [Arguments("modified")]
    [Arguments("missing")]
    public async Task A_node_with_no_local_tree_layer_reports_the_property_change_it_has(
        string item
    )
    {
        var entries = SvnStatusXml.Parse(Entry(item: item, props: "modified"), '/');

        await Assert.That(entries[0].PropertyStatus).IsEqualTo(PropertyStatus.Modified);
    }

    /// <summary>
    /// The property state is still read on a node that discards it, so a value this build cannot
    /// make sense of fails there too rather than being waved through.
    /// </summary>
    [Test]
    public async Task A_property_state_this_build_does_not_model_fails_even_where_it_is_discarded()
    {
        await Assert
            .That(() => SvnStatusXml.Parse(Entry(item: "added", props: "argle"), '/'))
            .Throws<SvnCommandException>()
            .WithMessageContaining("argle");
    }

    [Test]
    public async Task A_property_state_this_build_does_not_model_fails_the_read()
    {
        await Assert
            .That(() => SvnStatusXml.Parse(Entry(props: "argle"), '/'))
            .Throws<SvnCommandException>()
            .WithMessageContaining("argle");
    }

    /// <summary>
    /// SVN prints a property conflict in its second column with the first left blank. Subverted has
    /// no conflicted state on the property axis, so it escalates the whole node — the documented
    /// divergence, and the direction that over-reports rather than hiding a conflict.
    /// </summary>
    [Test]
    public async Task A_property_conflict_on_an_otherwise_clean_node_conflicts_the_node()
    {
        var entries = SvnStatusXml.Parse(Entry(item: "normal", props: "conflicted"), '/');

        await Assert.That(entries[0].Status).IsEqualTo(NodeStatus.Conflicted);
        await Assert.That(entries[0].PropertyStatus).IsEqualTo(PropertyStatus.Modified);
        await Assert.That(entries[0].IsConflicted).IsTrue();
    }

    [Test]
    public async Task A_text_conflict_conflicts_the_node_without_touching_the_property_axis()
    {
        var entries = SvnStatusXml.Parse(Entry(item: "conflicted", props: "none"), '/');

        await Assert.That(entries[0].Status).IsEqualTo(NodeStatus.Conflicted);
        await Assert.That(entries[0].PropertyStatus).IsEqualTo(PropertyStatus.Unmodified);
        await Assert.That(entries[0].IsConflicted).IsTrue();
    }

    [Test]
    public async Task A_node_with_neither_kind_of_conflict_is_not_conflicted()
    {
        var entries = SvnStatusXml.Parse(Entry(item: "modified", props: "modified"), '/');

        await Assert.That(entries[0].Status).IsEqualTo(NodeStatus.Modified);
        await Assert.That(entries[0].IsConflicted).IsFalse();
    }

    /// <summary>
    /// <c>svn status</c> reports no node kind at all, and inventing one from the path would be a
    /// guess. Nothing renders kind today; this pins that the fallback says so rather than defaulting
    /// everything to a file.
    /// </summary>
    [Test]
    public async Task Node_kind_is_reported_as_unknown_because_svn_status_does_not_carry_it()
    {
        var entries = SvnStatusXml.Parse(RealDocument, '\\');

        await Assert
            .That(entries.Select(entry => entry.Kind).Distinct())
            .IsEquivalentTo([NodeKind.Unknown]);
    }

    [Test]
    public async Task A_document_svn_did_not_write_as_xml_says_so()
    {
        await Assert
            .That(() => SvnStatusXml.Parse("svn: E155036: please upgrade", '/'))
            .Throws<SvnCommandException>()
            .WithMessageContaining("not XML");
    }

    [Test]
    public async Task A_document_that_is_not_a_status_document_says_which_one_it_is()
    {
        await Assert
            .That(() => SvnStatusXml.Parse("<info></info>", '/'))
            .Throws<SvnCommandException>()
            .WithMessageContaining("<info> document");
    }

    [Test]
    public async Task An_entry_with_no_path_fails_rather_than_landing_under_an_empty_one()
    {
        await Assert
            .That(() =>
                SvnStatusXml.Parse(
                    Document("""<entry><wc-status item="normal" props="none"/></entry>"""),
                    '/'
                )
            )
            .Throws<SvnCommandException>()
            .WithMessageContaining("no path attribute");
    }

    [Test]
    public async Task An_entry_with_no_status_names_the_path_it_could_not_answer_for()
    {
        await Assert
            .That(() => SvnStatusXml.Parse(Document("""<entry path="a.txt"></entry>"""), '/'))
            .Throws<SvnCommandException>()
            .WithMessageContaining("a.txt");
    }

    [Test]
    [Arguments("item")]
    [Arguments("props")]
    public async Task A_status_missing_either_axis_fails_rather_than_defaulting_it(string absent)
    {
        var present = absent == "item" ? """props="none" """ : """item="normal" """;

        await Assert
            .That(() =>
                SvnStatusXml.Parse(
                    Document($"""<entry path="a.txt"><wc-status {present}/></entry>"""),
                    '/'
                )
            )
            .Throws<SvnCommandException>()
            .WithMessageContaining($"no {absent} attribute");
    }

    [Test]
    public async Task A_revision_that_is_not_a_number_fails_rather_than_being_dropped()
    {
        await Assert
            .That(() =>
                SvnStatusXml.Parse(
                    Document(
                        """<entry path="a.txt"><wc-status item="normal" props="none" revision="HEAD"/></entry>"""
                    ),
                    '/'
                )
            )
            .Throws<SvnCommandException>()
            .WithMessageContaining("HEAD");
    }

    [Test]
    public async Task A_status_document_with_nothing_in_it_is_an_empty_working_copy_not_a_failure()
    {
        await Assert.That(SvnStatusXml.Parse("<status></status>", '/')).IsEmpty();
    }

    private static WorkingCopyEntry Single(
        IReadOnlyList<WorkingCopyEntry> entries,
        string relPath
    ) => entries.Single(entry => entry.RelPath == relPath);

    private static string Entry(string item = "normal", string props = "none") =>
        Document($"""<entry path="a.txt"><wc-status item="{item}" props="{props}"/></entry>""");

    /// <summary>Wraps entry markup in the target element SVN always puts around it.</summary>
    private static string Document(string entryMarkup) =>
        $"<status><target path=\".\">{entryMarkup}</target></status>";
}
