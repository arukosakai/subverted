using Subverted.Core;
using static Subverted.Svn.Tests.WcDbRowFactory;

namespace Subverted.Svn.Tests;

public sealed class NodeStatusResolverTests
{
    private static readonly DateTime RecordedTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly FileNode MatchingSnapshot = new(Length: 100, RecordedTime);

    [Test]
    public async Task Conflict_outranks_every_other_rule()
    {
        var row = Row(presence: "incomplete", opDepth: 2, hasConflict: true);

        await Assert
            .That(NodeStatusResolver.Resolve(row, snapshot: null))
            .IsEqualTo(NodeStatus.Conflicted);
    }

    [Test]
    public async Task Incomplete_outranks_local_layering()
    {
        var row = Row(presence: "incomplete", opDepth: 2);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.Incomplete);
    }

    [Test]
    [Arguments("base-deleted")]
    [Arguments("not-present")]
    public async Task Deleted_presences_report_deleted(string presence)
    {
        var row = Row(presence: presence, opDepth: 1, lowerOpDepth: 0);

        await Assert
            .That(NodeStatusResolver.Resolve(row, snapshot: null))
            .IsEqualTo(NodeStatus.Deleted);
    }

    /// <summary>
    /// <c>svn delete --keep-local</c> leaves the file where it was, and <c>svn status</c> still
    /// prints <c>D</c>: a delete is decided by its row, so a file on disk does not argue with it.
    /// </summary>
    [Test]
    public async Task A_delete_is_a_delete_with_the_file_still_on_disk()
    {
        var row = Row(presence: "base-deleted", opDepth: 2, lowerOpDepth: 0);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.Deleted);
    }

    [Test]
    public async Task An_operation_over_an_existing_base_is_a_replace()
    {
        var row = Row(opDepth: 2, lowerOpDepth: 0);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.Replaced);
    }

    /// <summary>
    /// <c>svn rm dircopy/sub/n.txt; svn copy file.txt dircopy/sub/n.txt</c> printed <c>R  +</c>.
    /// What it replaces is the copy at op_depth 1, and there is no BASE at all — so a rule that
    /// asked whether a BASE row exists called this an add.
    /// </summary>
    [Test]
    public async Task An_operation_over_a_copied_layer_is_a_replace_without_any_base()
    {
        var row = Row(opDepth: 2, lowerOpDepth: 1);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.Replaced);
    }

    [Test]
    public async Task An_operation_with_nothing_beneath_it_is_an_add()
    {
        var row = Row(opDepth: 2, lowerOpDepth: null, hasCopySource: false);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.Added);
    }

    /// <summary>
    /// An edited add still prints <c>A</c> and an edited copy <c>A  +</c>: the operation root is
    /// the change, so its content is never compared. A resized file proves the compare is skipped.
    /// </summary>
    [Test]
    public async Task An_edited_operation_root_is_still_an_add()
    {
        var row = Row(opDepth: 2, lowerOpDepth: null);

        await Assert
            .That(NodeStatusResolver.Resolve(row, new FileNode(Length: 7, RecordedTime)))
            .IsEqualTo(NodeStatus.Added);
    }

    [Test]
    public async Task Unrecognised_presence_escalates_rather_than_guessing()
    {
        var row = Row(presence: "something-svn-added-later", opDepth: 2);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.NeedsPristineCompare);
    }

    /// <summary>
    /// The same unknown presence one level down, where the row would otherwise pass for a node
    /// inside a copy and be settled by a recorded size and mtime it may not mean.
    /// </summary>
    [Test]
    public async Task Unrecognised_presence_inside_a_copy_escalates_too()
    {
        var row = Row(presence: "something-svn-added-later", opDepth: 1);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.NeedsPristineCompare);
    }

    [Test]
    public async Task Layering_is_detected_by_depth_not_by_presence()
    {
        var baseNode = Row(opDepth: 0);

        await Assert
            .That(NodeStatusResolver.Resolve(baseNode, MatchingSnapshot))
            .IsEqualTo(NodeStatus.Unmodified);
    }

    /// <summary>
    /// <c>dircopy/c5.txt</c>, untouched inside a copied directory, printed <c>   +</c> with a blank
    /// first column. Treating every op_depth &gt; 0 row as an add listed each file of a copied
    /// folder as <c>A</c>.
    /// </summary>
    [Test]
    public async Task An_untouched_node_inside_a_copy_is_unmodified()
    {
        var row = Row(opDepth: 1);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.Unmodified);
    }

    /// <summary><c>dircopy/c1.txt</c>, edited inside the copy: <c>M  +</c>.</summary>
    [Test]
    public async Task An_edited_node_inside_a_copy_is_modified()
    {
        var row = Row(opDepth: 1);

        await Assert
            .That(NodeStatusResolver.Resolve(row, new FileNode(Length: 7, RecordedTime)))
            .IsEqualTo(NodeStatus.Modified);
    }

    [Test]
    public async Task A_node_inside_a_copy_escalates_on_mtime_drift_like_a_base_node()
    {
        var row = Row(opDepth: 1);

        await Assert
            .That(NodeStatusResolver.Resolve(row, new FileNode(100, RecordedTime.AddSeconds(1))))
            .IsEqualTo(NodeStatus.NeedsPristineCompare);
    }

    /// <summary>
    /// <c>both/a/same.txt</c>: a directory replaced by a copy, with a child of the same name on
    /// both sides. It has a BASE row and a copy row and printed <c>   +</c>, not <c>R</c> — the
    /// replace is its parent's.
    /// </summary>
    [Test]
    public async Task A_node_inside_a_copy_is_not_a_replace_for_having_a_base_under_it()
    {
        var row = Row(opDepth: 1, lowerOpDepth: 0);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.Unmodified);
    }

    [Test]
    public async Task A_directory_inside_a_copy_is_unmodified()
    {
        var row = Row(opDepth: 1, kind: NodeKind.Directory, recordedSize: null);

        await Assert
            .That(NodeStatusResolver.Resolve(row, new DirectoryNode()))
            .IsEqualTo(NodeStatus.Unmodified);
    }

    /// <summary>
    /// Every shape of local layer was measured with its file gone — a plain add, a copy, a replace
    /// and a node inside a copy — and every one printed <c>!</c>. Calling any of them added hides
    /// that the file is gone from the one place the person is looking.
    /// </summary>
    [Test]
    [Arguments(2, null)]
    [Arguments(2, 0)]
    [Arguments(1, null)]
    public async Task A_local_layer_gone_from_disk_is_missing(int opDepth, int? lowerOpDepth)
    {
        var row = Row(opDepth: opDepth, lowerOpDepth: lowerOpDepth);

        await Assert
            .That(NodeStatusResolver.Resolve(row, snapshot: null))
            .IsEqualTo(NodeStatus.Missing);
    }

    [Test]
    public async Task Absent_from_disk_is_missing()
    {
        await Assert
            .That(NodeStatusResolver.Resolve(Row(), snapshot: null))
            .IsEqualTo(NodeStatus.Missing);
    }

    [Test]
    public async Task Present_directory_is_unmodified_without_consulting_recorded_state()
    {
        var row = Row(kind: NodeKind.Directory, recordedSize: null, recordedModTime: null);

        await Assert
            .That(NodeStatusResolver.Resolve(row, new DirectoryNode()))
            .IsEqualTo(NodeStatus.Unmodified);
    }

    [Test]
    public async Task Absent_directory_is_missing()
    {
        var row = Row(kind: NodeKind.Directory, recordedSize: null, recordedModTime: null);

        await Assert
            .That(NodeStatusResolver.Resolve(row, snapshot: null))
            .IsEqualTo(NodeStatus.Missing);
    }

    /// <summary>
    /// A directory standing where a versioned file should be. Without this rule the node falls
    /// through to "a directory has no content to compare" and reports clean, which is the
    /// dangerous direction: the working copy is broken and the listing says nothing.
    /// </summary>
    [Test]
    public async Task A_directory_where_a_versioned_file_belongs_is_obstructed()
    {
        var row = Row(kind: NodeKind.File);

        await Assert
            .That(NodeStatusResolver.Resolve(row, new DirectoryNode()))
            .IsEqualTo(NodeStatus.Obstructed);
    }

    /// <summary>
    /// The other direction. It used to reach the content compare, where a directory row has no
    /// recorded size and so reported <see cref="NodeStatus.NeedsPristineCompare"/> — which rendered
    /// as <c>~</c> and looked right for entirely the wrong reason.
    /// </summary>
    [Test]
    public async Task A_file_where_a_versioned_directory_belongs_is_obstructed()
    {
        var row = Row(kind: NodeKind.Directory, recordedSize: null, recordedModTime: null);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.Obstructed);
    }

    /// <summary>
    /// The negative cases, so the rule cannot be satisfied by calling everything obstructed. A
    /// symlink is included deliberately: the index reports what the link resolves to, so treating
    /// a kind difference as an obstruction there would obstruct every symlink in the tree.
    /// </summary>
    [Test]
    [Arguments(NodeKind.Symlink)]
    [Arguments(NodeKind.Unknown)]
    public async Task A_kind_SVN_does_not_call_obstructed_is_left_alone(NodeKind kind)
    {
        var row = Row(kind: kind);

        await Assert
            .That(NodeStatusResolver.Resolve(row, new DirectoryNode()))
            .IsEqualTo(NodeStatus.Unmodified);
    }

    [Test]
    public async Task A_file_on_disk_for_a_versioned_file_is_not_obstructed()
    {
        var row = Row(kind: NodeKind.File);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.Unmodified);
    }

    /// <summary>
    /// This was once asserted the other way, as a BASE-only rule — it is not. An added file with a
    /// directory in its place, a copy likewise, and an added directory with a file in its place all
    /// printed <c>~</c>, so a broken add was being reported as a healthy one.
    /// </summary>
    [Test]
    [Arguments(2, null)]
    [Arguments(2, 0)]
    [Arguments(1, null)]
    public async Task A_local_layer_with_the_wrong_kind_on_disk_is_obstructed(
        int opDepth,
        int? lowerOpDepth
    )
    {
        var row = Row(kind: NodeKind.File, opDepth: opDepth, lowerOpDepth: lowerOpDepth);

        await Assert
            .That(NodeStatusResolver.Resolve(row, new DirectoryNode()))
            .IsEqualTo(NodeStatus.Obstructed);
    }

    [Test]
    public async Task Matching_size_and_mtime_prove_the_file_is_clean()
    {
        await Assert
            .That(NodeStatusResolver.Resolve(Row(), MatchingSnapshot))
            .IsEqualTo(NodeStatus.Unmodified);
    }

    [Test]
    public async Task Differing_size_proves_a_modification()
    {
        var snapshot = MatchingSnapshot with { Length = 101 };

        await Assert
            .That(NodeStatusResolver.Resolve(Row(), snapshot))
            .IsEqualTo(NodeStatus.Modified);
    }

    [Test]
    public async Task Differing_mtime_alone_proves_nothing_and_escalates()
    {
        var snapshot = MatchingSnapshot with { LastWriteTimeUtc = RecordedTime.AddSeconds(1) };

        await Assert
            .That(NodeStatusResolver.Resolve(Row(), snapshot))
            .IsEqualTo(NodeStatus.NeedsPristineCompare);
    }

    [Test]
    public async Task A_single_microsecond_of_drift_is_still_drift()
    {
        var snapshot = MatchingSnapshot with { LastWriteTimeUtc = RecordedTime.AddMicroseconds(1) };

        await Assert
            .That(NodeStatusResolver.Resolve(Row(), snapshot))
            .IsEqualTo(NodeStatus.NeedsPristineCompare);
    }

    [Test]
    [Arguments(null, 1_704_067_200_000_000L)]
    [Arguments(100L, null)]
    [Arguments(null, null)]
    public async Task Missing_recorded_state_escalates(long? size, long? modTime)
    {
        var row = Row(recordedSize: size, recordedModTime: modTime);

        await Assert
            .That(NodeStatusResolver.Resolve(row, MatchingSnapshot))
            .IsEqualTo(NodeStatus.NeedsPristineCompare);
    }

    [Test]
    public async Task Size_is_checked_before_mtime_so_a_resize_is_never_downgraded()
    {
        var snapshot = new FileNode(Length: 101, RecordedTime.AddSeconds(1));

        await Assert
            .That(NodeStatusResolver.Resolve(Row(), snapshot))
            .IsEqualTo(NodeStatus.Modified);
    }

    /// <summary>
    /// SVN stores mtimes as APR time: microseconds since the Unix epoch. Getting the unit wrong
    /// would silently mark an entire working copy as needing a content compare.
    /// </summary>
    [Test]
    [Arguments(0L, 0)]
    [Arguments(1_000_000L, 1)]
    public async Task Apr_time_is_microseconds_since_the_unix_epoch(
        long aprTime,
        int secondsPastEpoch
    )
    {
        var row = Row(recordedModTime: aprTime);
        var snapshot = new FileNode(Length: 100, DateTime.UnixEpoch.AddSeconds(secondsPastEpoch));

        await Assert
            .That(NodeStatusResolver.Resolve(row, snapshot))
            .IsEqualTo(NodeStatus.Unmodified);
    }
}
