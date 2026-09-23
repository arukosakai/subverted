using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// The list someone reads before taking files off disk. The split down the middle is the rule that
/// matters: a versioned file comes back with <c>sv revert</c> until the delete is committed, and an
/// unversioned one has no pristine and is simply gone.
/// </summary>
public sealed class RemovalPreviewTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    private static readonly string Root = Path.GetFullPath("/wc");

    [Test]
    public async Task A_versioned_file_is_listed_as_recoverable()
    {
        var preview = Of(Modified("art/hero.png"));

        await Assert.That(preview.Recoverable.Count).IsEqualTo(1);
        await Assert.That(preview.Unrecoverable).IsEmpty();
        await Assert.That(preview.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The commonest removal of all, and the one that was broken: a file nobody has edited is
    /// absent from a default listing, so <c>sv rm</c> saw nothing under the target and said
    /// "nothing to remove" while the file sat there. Found by running it.
    /// </summary>
    [Test]
    public async Task A_clean_file_is_listed_as_recoverable_like_any_other_versioned_one()
    {
        var preview = Of(Modified("art/hero.png") with { Status = NodeStatus.Unmodified });

        await Assert.That(preview.Recoverable.Count).IsEqualTo(1);
        await Assert.That(preview.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The other half of the same bug: the listing has to be asked for with both switches on, or
    /// the clean and ignored nodes above never reach the preview to be classified at all.
    /// </summary>
    [Test]
    public async Task The_listing_a_removal_needs_holds_the_clean_and_the_ignored()
    {
        var request = RemovalPreview.ListingFor(Root);

        await Assert.That(request.WorkingCopyPath).IsEqualTo(Root);
        await Assert.That(request.IncludeUnmodified).IsTrue();
        await Assert.That(request.IncludeIgnored).IsTrue();
    }

    [Test]
    public async Task An_unversioned_file_is_listed_as_unrecoverable()
    {
        var preview = Of(Unversioned("art/scratch.txt"));

        await Assert.That(preview.Recoverable).IsEmpty();
        await Assert.That(preview.Unrecoverable.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Ignored files are hidden from an ordinary listing, but one named here is about to be
    /// unlinked with nothing behind it — counting it as recoverable would understate the damage.
    /// </summary>
    [Test]
    public async Task An_ignored_file_is_unrecoverable_too()
    {
        var preview = Of(Unversioned("art/hero.png.bak") with { Status = NodeStatus.Ignored });

        await Assert.That(preview.Unrecoverable.Count).IsEqualTo(1);
        await Assert.That(preview.Recoverable).IsEmpty();
    }

    /// <summary>
    /// An external is a working copy of its own. Removing it is a change to the parent's
    /// <c>svn:externals</c> property, not a delete, so it belongs to neither list.
    /// </summary>
    [Test]
    public async Task An_external_is_in_neither_list()
    {
        var preview = Of(Modified("lib") with { Status = NodeStatus.External });

        await Assert.That(preview.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Both_kinds_are_counted_together()
    {
        var preview = Of(Modified("art/hero.png"), Unversioned("art/scratch.txt"));

        await Assert.That(preview.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Only_the_nodes_under_the_named_target_are_listed()
    {
        var preview = RemovalPreview.Of(
            Status(Modified("art/hero.png"), Modified("src/a.txt")),
            [Path.Combine(Root, "art")],
            Sensitive
        );

        await Assert.That(preview.Recoverable.Count).IsEqualTo(1);
        await Assert.That(preview.Recoverable[0].RelPath).IsEqualTo("art/hero.png");
    }

    [Test]
    public async Task Nothing_under_the_target_is_nothing_to_remove()
    {
        var preview = RemovalPreview.Of(
            Status(Modified("src/a.txt")),
            [Path.Combine(Root, "art")],
            Sensitive
        );

        await Assert.That(preview.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Recoverable_nodes_are_shown_in_the_status_layout()
    {
        await Assert
            .That(Of(Modified("art/hero.png")).Lines)
            .IsEquivalentTo(["M       art/hero.png"]);
    }

    /// <summary>
    /// The warning only appears when there is something to warn about — printing it over an empty
    /// list is how people learn to skip it.
    /// </summary>
    [Test]
    public async Task Nothing_unrecoverable_prints_no_warning()
    {
        await Assert
            .That(Of(Modified("art/hero.png")).Lines.Any(line => line.Contains("bring them back")))
            .IsFalse();
    }

    [Test]
    public async Task The_unrecoverable_ones_are_called_out_below_with_their_count()
    {
        var lines = Of(
            Modified("art/hero.png"),
            Unversioned("art/one.txt"),
            Unversioned("art/two.txt")
        ).Lines;

        await Assert.That(lines[0]).IsEqualTo("M       art/hero.png");
        await Assert.That(lines[1]).IsEmpty();
        await Assert.That(lines[2]).Contains("2 of these are not in SVN");
        await Assert.That(lines[3]).IsEqualTo("?       art/one.txt");
        await Assert.That(lines[4]).IsEqualTo("?       art/two.txt");
    }

    /// <summary>
    /// With nothing recoverable there is no list above to separate from, so the blank line would be
    /// a stray one at the top of the output.
    /// </summary>
    [Test]
    public async Task Only_unrecoverable_nodes_start_straight_at_the_warning()
    {
        var lines = Of(Unversioned("art/scratch.txt")).Lines;

        await Assert.That(lines[0]).Contains("1 of these are not in SVN");
        await Assert.That(lines.Count).IsEqualTo(2);
    }

    private static RemovalPreview Of(params WorkingCopyEntry[] entries) =>
        RemovalPreview.Of(Status(entries), [Root], Sensitive);

    private static StatusResponse Status(params WorkingCopyEntry[] entries) =>
        new(
            new WorkingCopyInfo(Root, "https://svn.example/repo", "uuid-1", 31),
            entries,
            ServedFromWarmIndex: true,
            ServerElapsedMilliseconds: 0,
            UnfinishedOperations: 0,
            UnrecordedMoves: []
        );

    private static WorkingCopyEntry Modified(string relPath) =>
        new(
            relPath,
            NodeKind.File,
            NodeStatus.Modified,
            PropertyStatus.Unmodified,
            Revision: 1,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );

    private static WorkingCopyEntry Unversioned(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Unversioned,
            Revision = null,
        };
}
