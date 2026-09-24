using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

public sealed class RemovalReportTests
{
    private static readonly string Root = Path.GetFullPath("/wc");

    private static readonly RemovalPreview NothingSilent = Confirmed(Entry("art/hero.png"));

    [Test]
    public async Task Svns_own_notification_is_passed_through()
    {
        var lines = RemovalReport.Lines(
            new DeleteResponse("D         art/hero.png\n"),
            NothingSilent
        );

        await Assert.That(lines).IsEquivalentTo(["D         art/hero.png"]);
    }

    /// <summary>
    /// SVN says nothing whatever about an unversioned file it unlinked, so without this line the
    /// person who removed an untracked folder is looking at an empty screen.
    /// </summary>
    [Test]
    public async Task Files_svn_removed_silently_are_counted_out_loud()
    {
        var confirmed = Confirmed(
            Entry("art") with
            {
                Kind = NodeKind.Directory,
            },
            Entry("art/one.txt") with
            {
                Status = NodeStatus.Unversioned,
            },
            Entry("art/two.txt.bak") with
            {
                Status = NodeStatus.Ignored,
            }
        );

        var lines = RemovalReport.Lines(new DeleteResponse(string.Empty), confirmed);

        await Assert
            .That(lines)
            .IsEquivalentTo([
                "2 file(s) SVN does not track were removed from disk. Nothing here can bring those back.",
            ]);
    }

    /// <summary>
    /// An add is lost for good too, but SVN prints a <c>D</c> for it, so counting it here would
    /// say it twice and call it untracked, which it was not.
    /// </summary>
    [Test]
    public async Task An_add_svn_printed_is_not_counted_as_silent()
    {
        var confirmed = Confirmed(Entry("art/new.png") with { Status = NodeStatus.Added });

        var lines = RemovalReport.Lines(new DeleteResponse("D         art/new.png\n"), confirmed);

        await Assert.That(lines).IsEquivalentTo(["D         art/new.png"]);
    }

    /// <summary>
    /// Measured on 1.8.15: an external inside a deleted folder goes with its edits, and SVN prints
    /// no line for it at all.
    /// </summary>
    [Test]
    public async Task An_external_svn_removed_silently_is_said_out_loud()
    {
        var confirmed = Confirmed(
            Entry("vendor") with
            {
                Kind = NodeKind.Directory,
            },
            Entry("vendor/lib") with
            {
                Kind = NodeKind.Directory,
                Status = NodeStatus.External,
            }
        );

        var lines = RemovalReport.Lines(new DeleteResponse("D         vendor\n"), confirmed);

        await Assert
            .That(lines)
            .IsEquivalentTo([
                "D         vendor",
                "1 external checkout(s) were removed from disk with everything in them, their own "
                    + "edits included. Nothing here can bring those edits back.",
            ]);
    }

    [Test]
    public async Task A_removal_that_did_nothing_says_so_rather_than_printing_nothing()
    {
        await Assert
            .That(RemovalReport.Lines(new DeleteResponse(string.Empty), NothingSilent))
            .IsEquivalentTo(["nothing removed"]);
    }

    private static RemovalPreview Confirmed(params WorkingCopyEntry[] entries) =>
        RemovalPreview.Of(
            new StatusResponse(
                new WorkingCopyInfo(Root, "https://svn.example/repo", "uuid-1", 31),
                entries,
                ServedFromWarmIndex: true,
                ServerElapsedMilliseconds: 0,
                UnfinishedOperations: 0,
                UnrecordedMoves: []
            ),
            [Path.Combine(Root, entries[0].RelPath)],
            StringComparison.Ordinal
        );

    private static WorkingCopyEntry Entry(string relPath) =>
        new(
            relPath,
            NodeKind.File,
            NodeStatus.Unmodified,
            PropertyStatus.Unmodified,
            Revision: 1,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );
}
