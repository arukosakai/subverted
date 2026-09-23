using static Subverted.Svn.Tests.WcDbRowFactory;

namespace Subverted.Svn.Tests;

public sealed class WcDbRowTests
{
    [Test]
    [Arguments("excluded")]
    [Arguments("server-excluded")]
    public async Task Excluded_nodes_are_absent_at_any_depth(string presence)
    {
        await Assert.That(Row(presence: presence, opDepth: 2).IsAbsentFromWorkingCopy).IsTrue();
    }

    [Test]
    public async Task Not_present_at_base_is_a_tombstone_and_therefore_absent()
    {
        await Assert
            .That(Row(presence: "not-present", opDepth: 0).IsAbsentFromWorkingCopy)
            .IsTrue();
    }

    /// <summary>
    /// A not-present row layered over BASE is a local delete, which must still be reported —
    /// dropping it here would hide every pending deletion from the status list.
    /// </summary>
    [Test]
    public async Task Not_present_above_base_is_a_local_delete_and_stays_visible()
    {
        await Assert
            .That(Row(presence: "not-present", opDepth: 1).IsAbsentFromWorkingCopy)
            .IsFalse();
    }

    [Test]
    [Arguments("normal")]
    [Arguments("incomplete")]
    [Arguments("base-deleted")]
    public async Task Materialised_presences_are_present(string presence)
    {
        await Assert.That(Row(presence: presence).IsAbsentFromWorkingCopy).IsFalse();
    }

    /// <summary>
    /// Setting <c>svn:eol-style</c> on a committed file leaves <c>translated_size = -1</c> in a
    /// real wc.db. Read as a size it never equals the file's length, so the node reports Modified
    /// when nothing about its content changed.
    /// </summary>
    [Test]
    [Arguments(-1L)]
    [Arguments(null)]
    public async Task An_invalidated_size_is_not_a_recorded_size(long? stored)
    {
        await Assert.That(Row(recordedSize: stored).RecordedSize).IsNull();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(6L)]
    public async Task A_stored_size_of_zero_or_more_is_a_recorded_size(long stored)
    {
        await Assert.That(Row(recordedSize: stored).RecordedSize).IsEqualTo(stored);
    }

    /// <summary>
    /// op_depth is the depth of the operation that wrote the row, so a row whose op_depth is its
    /// own path's depth is that operation. <c>plaindir/y.txt</c> added with its parent sat at
    /// op_depth 2 — each plain add is a root of its own — where copied children sit at the copy's.
    /// </summary>
    [Test]
    [Arguments("assets/hero.png", 2, true)]
    [Arguments("assets/hero.png", 1, false)]
    [Arguments("assets/hero.png", 0, false)]
    [Arguments("hero.png", 1, true)]
    [Arguments("a/b/c/hero.png", 4, true)]
    [Arguments("a/b/c/hero.png", 3, false)]
    public async Task A_row_is_an_operation_root_when_its_op_depth_is_its_own_depth(
        string relPath,
        int opDepth,
        bool expected
    )
    {
        await Assert
            .That(Row(relPath: relPath, opDepth: opDepth).IsOperationRoot)
            .IsEqualTo(expected);
    }

    [Test]
    [Arguments("normal", 1, true)]
    [Arguments("normal", 2, false)]
    [Arguments("normal", 0, false)]
    [Arguments("base-deleted", 1, false)]
    public async Task A_row_is_inside_a_copy_when_it_is_normal_and_below_its_operation_root(
        string presence,
        int opDepth,
        bool expected
    )
    {
        await Assert
            .That(Row(presence: presence, opDepth: opDepth).IsWithinCopy)
            .IsEqualTo(expected);
    }
}
