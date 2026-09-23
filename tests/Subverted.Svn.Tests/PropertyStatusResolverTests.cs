using System.Text;
using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// The blobs here were read out of a purpose-built SVN 1.8.15 working copy, and the expected
/// results are what that copy's <c>svn status</c> printed in its second column. Where a case is a
/// hand-written variation it says so.
/// </summary>
public sealed class PropertyStatusResolverTests
{
    /// <summary>
    /// Each row is one node of the fixture, named for the status SVN printed for it.
    /// </summary>
    [Test]
    // propmod.txt — ' M', a property added on top of the committed set.
    [Arguments(
        "(svn:mime-type text/plain svn:needs-lock 1 *)",
        "(svn:needs-lock 1 *)",
        PropertyStatus.Modified
    )]
    // propdel.txt — ' M'. SVN records the deletion as an empty skel, not as NULL.
    [Arguments("()", "(svn:needs-lock 1 *)", PropertyStatus.Modified)]
    // clean.txt — ' C' on a property conflict; the sets differ, which is all this axis claims.
    [Arguments(
        "(svn:mime-type text/x-local svn:needs-lock 1 *)",
        "(svn:mime-type application/octet-stream svn:needs-lock 1 *)",
        PropertyStatus.Modified
    )]
    // sub — ' M' on a directory; svn:ignore is a property like any other.
    [Arguments("(svn:ignore 5 dist\n)", "(svn:ignore 6 build\n)", PropertyStatus.Modified)]
    // equalprop.txt — clean. Setting a property to the value it already had prunes the row's blob.
    [Arguments(null, "(svn:needs-lock 1 *)", PropertyStatus.Unmodified)]
    // textonly.txt — 'M ', content changed and properties untouched.
    [Arguments(null, null, PropertyStatus.Unmodified)]
    public async Task Fixture_nodes_resolve_to_the_column_svn_printed(
        string? working,
        string? pristine,
        PropertyStatus expected
    )
    {
        await Assert.That(Resolve(working, pristine)).IsEqualTo(expected);
    }

    /// <summary>
    /// added.txt printed 'A ' and copied-propmod.txt printed 'A  +', both with a blank property
    /// column, even though the copy's properties genuinely differ from its source's — verified
    /// with <c>svn diff</c>. The add is what carries them, so op_depth decides this before the
    /// sets are ever compared.
    /// </summary>
    [Test]
    [Arguments(0, PropertyStatus.Modified)]
    [Arguments(1, PropertyStatus.Unmodified)]
    public async Task A_local_add_subsumes_its_own_properties(int opDepth, PropertyStatus expected)
    {
        var resolved = Resolve(
            "(svn:mime-type text/plain svn:needs-lock 1 *)",
            "(svn:needs-lock 1 *)",
            opDepth
        );

        await Assert.That(resolved).IsEqualTo(expected);
    }

    /// <summary>
    /// <c>dircopy/c4.txt</c>, a property set on a file inside a copied directory, printed
    /// <c> M +</c>. Only the operation root carries its properties; everything below it has the
    /// copy's recorded set to differ from, the same way a BASE node does.
    /// </summary>
    [Test]
    [Arguments(true, PropertyStatus.Modified)]
    [Arguments(false, PropertyStatus.Unmodified)]
    public async Task A_node_inside_a_copy_compares_its_properties(
        bool isWithinCopy,
        PropertyStatus expected
    )
    {
        var resolved = PropertyStatusResolver.Resolve(
            opDepth: 1,
            isWithinCopy,
            ToBlob("(p 1 v)"),
            ToBlob("()")
        );

        await Assert.That(resolved).IsEqualTo(expected);
    }

    /// <summary>
    /// A NULL in ACTUAL_NODE.properties means "no local property change", which is a different
    /// claim from the empty skel that means "this node has no properties".
    /// </summary>
    [Test]
    public async Task No_recorded_change_beats_an_unreadable_pristine_set()
    {
        await Assert
            .That(Resolve(working: null, pristine: "(svn:needs-lock 1 *"))
            .IsEqualTo(PropertyStatus.Unmodified);
    }

    /// <summary>
    /// Hand-written truncations. Reporting a change we cannot read costs a spurious ' M'; the
    /// other way round hides a node the user still has to commit.
    /// </summary>
    [Test]
    [Arguments("(svn:needs-lock 1 *", "(svn:needs-lock 1 *)")]
    [Arguments("(svn:needs-lock 1 *)", "(svn:needs-lock 1 *")]
    public async Task An_unreadable_skel_is_reported_as_modified_rather_than_guessed_clean(
        string working,
        string pristine
    )
    {
        await Assert.That(Resolve(working, pristine)).IsEqualTo(PropertyStatus.Modified);
    }

    [Test]
    public async Task A_property_removed_from_the_recorded_set_is_a_change()
    {
        var resolved = Resolve(
            working: "(svn:needs-lock 1 *)",
            pristine: "(svn:mime-type text/plain svn:needs-lock 1 *)"
        );

        await Assert.That(resolved).IsEqualTo(PropertyStatus.Modified);
    }

    /// <summary>
    /// Same number of properties either side, so only the names distinguish these two sets.
    /// </summary>
    [Test]
    public async Task A_property_renamed_at_the_same_count_is_a_change()
    {
        var resolved = Resolve(
            working: "(svn:mime-type text/plain)",
            pristine: "(svn:needs-lock 1 *)"
        );

        await Assert.That(resolved).IsEqualTo(PropertyStatus.Modified);
    }

    [Test]
    public async Task The_same_property_at_a_different_value_is_a_change()
    {
        var resolved = Resolve(
            working: "(svn:mime-type text/x-local)",
            pristine: "(svn:mime-type application/octet-stream)"
        );

        await Assert.That(resolved).IsEqualTo(PropertyStatus.Modified);
    }

    /// <summary>
    /// Hand-written: SVN writes these alphabetically, but the format does not promise it and a
    /// reordering is not a change.
    /// </summary>
    [Test]
    public async Task Stored_order_is_not_part_of_the_comparison()
    {
        var resolved = Resolve(
            working: "(svn:needs-lock 1 * svn:mime-type text/plain)",
            pristine: "(svn:mime-type text/plain svn:needs-lock 1 *)"
        );

        await Assert.That(resolved).IsEqualTo(PropertyStatus.Unmodified);
    }

    [Test]
    public async Task An_identical_set_is_not_a_change()
    {
        var resolved = Resolve(
            working: "(svn:mime-type text/plain svn:needs-lock 1 *)",
            pristine: "(svn:mime-type text/plain svn:needs-lock 1 *)"
        );

        await Assert.That(resolved).IsEqualTo(PropertyStatus.Unmodified);
    }

    /// <summary>
    /// An empty skel against a node that never had properties: nothing was removed, so nothing
    /// changed. Reporting this as a deletion would mark clean nodes across a whole checkout.
    /// </summary>
    [Test]
    public async Task An_empty_set_against_no_recorded_set_is_not_a_change()
    {
        await Assert.That(Resolve("()", pristine: null)).IsEqualTo(PropertyStatus.Unmodified);
    }

    private static PropertyStatus Resolve(string? working, string? pristine, int opDepth = 0) =>
        PropertyStatusResolver.Resolve(
            opDepth,
            isWithinCopy: false,
            ToBlob(working),
            ToBlob(pristine)
        );

    private static byte[]? ToBlob(string? skel) =>
        skel is null ? null : Encoding.UTF8.GetBytes(skel);
}
