using System.Text;
using Subverted.Core;

namespace Subverted.Svn.Tests.ContextDiff;

/// <summary>
/// When the working-copy diff may be written from the pristine: only for a file BASE holds with
/// nothing layered over it and nothing but its text changed — every other shape svn prints differently.
/// </summary>
public sealed class PristineBaseRowTests
{
    private static readonly PristineBaseRow Comparable = new(
        OpDepth: 0,
        Presence: "normal",
        Kind: NodeKind.File,
        Revision: 4,
        Checksum: "$sha1$" + new string('a', 40),
        PristineProperties: null,
        WorkingProperties: null,
        HasConflict: false,
        PristineSize: 12,
        PristineIsPlain: true
    );

    [Test]
    public async Task A_plain_edit_of_a_base_file_is_compared_as_committed()
    {
        await Assert.That(Comparable.ComparableLineEndings).IsEqualTo(LineEndingStyle.AsCommitted);
    }

    [Test]
    public async Task Its_eol_style_says_how_the_working_file_is_normalised()
    {
        var row = Comparable with { PristineProperties = Skel("(svn:eol-style native)") };

        await Assert.That(row.ComparableLineEndings).IsEqualTo(LineEndingStyle.Native);
    }

    [Test]
    public async Task An_add_copy_or_delete_over_base_is_left_to_svn()
    {
        await Assert.That((Comparable with { OpDepth = 1 }).ComparableLineEndings).IsNull();
    }

    [Test]
    [Arguments("not-present")]
    [Arguments("excluded")]
    [Arguments("incomplete")]
    public async Task A_base_node_that_is_not_normal_is_left_to_svn(string presence)
    {
        await Assert.That((Comparable with { Presence = presence }).ComparableLineEndings).IsNull();
    }

    [Test]
    public async Task A_folder_is_left_to_svn()
    {
        await Assert.That((Comparable with { Kind = NodeKind.Directory }).ComparableLineEndings).IsNull();
    }

    [Test]
    public async Task A_node_with_no_base_revision_to_name_is_left_to_svn()
    {
        await Assert.That((Comparable with { Revision = null }).ComparableLineEndings).IsNull();
    }

    [Test]
    public async Task A_conflicted_file_is_left_to_svn()
    {
        await Assert.That((Comparable with { HasConflict = true }).ComparableLineEndings).IsNull();
    }

    [Test]
    public async Task A_checksum_the_pristine_table_does_not_hold_is_left_to_svn()
    {
        await Assert.That((Comparable with { PristineSize = null }).ComparableLineEndings).IsNull();
    }

    [Test]
    public async Task A_pristine_stored_in_any_other_way_is_left_to_svn()
    {
        await Assert.That((Comparable with { PristineIsPlain = false }).ComparableLineEndings).IsNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("$md5$0123")]
    public async Task A_checksum_that_names_no_pristine_is_left_to_svn(string? checksum)
    {
        await Assert.That((Comparable with { Checksum = checksum }).ComparableLineEndings).IsNull();
    }

    [Test]
    public async Task A_property_change_is_left_to_svn_which_prints_it()
    {
        var row = Comparable with { WorkingProperties = Skel("(custom:flag yes)") };

        await Assert.That(row.ComparableLineEndings).IsNull();
    }

    [Test]
    public async Task Working_properties_equal_to_the_pristine_ones_are_no_change()
    {
        var row = Comparable with
        {
            PristineProperties = Skel("(svn:eol-style LF)"),
            WorkingProperties = Skel("(svn:eol-style LF)"),
        };

        await Assert.That(row.ComparableLineEndings).IsEqualTo(LineEndingStyle.Lf);
    }

    [Test]
    public async Task Properties_that_make_the_text_incomparable_are_left_to_svn()
    {
        var row = Comparable with { PristineProperties = Skel("(svn:keywords Id)") };

        await Assert.That(row.ComparableLineEndings).IsNull();
    }

    [Test]
    public async Task Properties_that_do_not_parse_are_not_guessed_at()
    {
        var row = Comparable with { PristineProperties = Skel("not a skel") };

        await Assert.That(row.ComparableLineEndings).IsNull();
    }

    private static byte[] Skel(string text) => Encoding.ASCII.GetBytes(text);
}
