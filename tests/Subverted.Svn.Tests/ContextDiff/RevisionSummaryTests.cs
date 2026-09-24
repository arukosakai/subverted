using TUnit.Assertions.Enums;

namespace Subverted.Svn.Tests.ContextDiff;

/// <summary>
/// What History reads before it writes a diff itself: <c>svn diff --summarize --xml -c N</c> and
/// <c>svn proplist -v --xml</c>, in the shapes 1.8.15 printed them.
/// </summary>
public sealed class RevisionSummaryTests
{
    [Test]
    public async Task Each_summarised_path_keeps_what_happened_to_its_text_and_properties_and_its_kind()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <diff>
            <paths>
            <path
               props="modified"
               kind="file"
               item="none">file:///r/props.txt</path>
            <path
               item="added"
               props="none"
               kind="dir">file:///r/sub</path>
            </paths>
            </diff>
            """;

        var changes = SvnDiffSummary.Changes(xml);

        await Assert
            .That(changes)
            .IsEquivalentTo(
                [
                    new SummarisedPath("none", "modified", "file"),
                    new SummarisedPath("added", "none", "dir"),
                ],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_missing_attribute_reads_as_empty_and_no_paths_as_none()
    {
        await Assert
            .That(SvnDiffSummary.Changes("<diff><paths><path>x</path></paths></diff>"))
            .IsEquivalentTo([new SummarisedPath("", "", "")]);
        await Assert.That(SvnDiffSummary.Changes("<diff><paths></paths></diff>")).IsEmpty();
    }

    [Test]
    public async Task A_summary_that_is_not_xml_is_no_answer()
    {
        await Assert.That(SvnDiffSummary.Changes("svn: E170000")).IsNull();
    }

    [Test]
    public async Task Only_a_file_whose_text_alone_changed_is_a_text_edit()
    {
        await Assert
            .That(new SummarisedPath("modified", "none", "file").IsTextEditOfAFile)
            .IsTrue();
        await Assert.That(new SummarisedPath("added", "none", "file").IsTextEditOfAFile).IsFalse();
        await Assert
            .That(new SummarisedPath("modified", "modified", "file").IsTextEditOfAFile)
            .IsFalse();
        await Assert
            .That(new SummarisedPath("modified", "none", "dir").IsTextEditOfAFile)
            .IsFalse();
    }

    [Test]
    public async Task A_property_list_gives_each_name_with_its_value()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <properties>
            <target
               path="file:///r/kw.txt">
            <property
               name="svn:keywords">Id Rev</property>
            <property
               name="svn:eol-style">native</property>
            </target>
            </properties>
            """;

        await Assert
            .That(SvnPropertyListXml.Parse(xml))
            .IsEquivalentTo(
                [
                    new SvnProperty("svn:keywords", "Id Rev"),
                    new SvnProperty("svn:eol-style", "native"),
                ],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_target_with_no_properties_lists_none_and_a_nameless_one_reads_as_empty()
    {
        await Assert.That(SvnPropertyListXml.Parse("<properties>\n</properties>")).IsEmpty();
        await Assert
            .That(
                SvnPropertyListXml.Parse(
                    "<properties><target><property>v</property></target></properties>"
                )
            )
            .IsEquivalentTo([new SvnProperty("", "v")]);
    }

    [Test]
    public async Task A_property_list_that_is_not_xml_is_no_answer()
    {
        await Assert.That(SvnPropertyListXml.Parse("svn: E200009")).IsNull();
    }
}
