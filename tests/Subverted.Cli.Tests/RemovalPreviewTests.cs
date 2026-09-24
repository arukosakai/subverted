using Subverted.Core;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// The list someone reads before taking files off disk. Its rules are the app's Delete rules, so
/// these tests pin the CLI to them: what is refused, and what is lost for good.
/// </summary>
public sealed class RemovalPreviewTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    private const string NewLine = "\n";

    private static readonly string Root = Path.GetFullPath("/wc");

    [Test]
    public async Task A_clean_file_is_recoverable()
    {
        var preview = Of(Clean("art/hero.png"));

        await Assert
            .That(preview.Recoverable.Select(node => node.Entry.RelPath))
            .IsEquivalentTo(["art/hero.png"]);
        await Assert.That(preview.Unrecoverable).IsEmpty();
        await Assert.That(preview.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Revert brings the file back at its pristine, not with the edit: the edit is what is lost.
    /// </summary>
    [Test]
    public async Task An_edited_file_is_unrecoverable_because_its_edits_go_with_it()
    {
        var preview = Of(Clean("art/hero.png") with { Status = NodeStatus.Modified });

        await Assert.That(preview.Recoverable).IsEmpty();
        await Assert.That(preview.Unrecoverable.Count).IsEqualTo(1);
        await Assert
            .That(preview.Unrecoverable[0].Loss.What)
            .IsEqualTo("Deleted from disk, and its edits with it");
    }

    /// <summary>
    /// Measured on 1.8.15: <c>svn delete --force</c> of an <c>A</c> file removes it from disk, and
    /// with nothing committed there is no pristine to bring it back from.
    /// </summary>
    [Test]
    public async Task An_added_file_is_unrecoverable_because_it_was_never_committed()
    {
        var preview = Of(Clean("art/new.png") with { Status = NodeStatus.Added });

        await Assert.That(preview.Recoverable).IsEmpty();
        await Assert.That(preview.Unrecoverable.Count).IsEqualTo(1);
        await Assert
            .That(preview.Unrecoverable[0].Loss.What)
            .IsEqualTo("Deleted from disk; it was never committed, so nothing can bring it back");
    }

    /// <summary>
    /// Measured on 1.8.15: deleting a folder removes an external inside it, with its edits and
    /// unversioned files, and SVN prints no line for any of it.
    /// </summary>
    [Test]
    public async Task An_external_inside_a_removed_folder_is_unrecoverable()
    {
        var preview = Of(
            Folder("vendor"),
            Folder("vendor/lib") with
            {
                Status = NodeStatus.External,
            }
        );

        await Assert
            .That(preview.Recoverable.Select(node => node.Entry.RelPath))
            .IsEquivalentTo(["vendor"]);
        await Assert
            .That(preview.Unrecoverable.Select(node => node.Entry.RelPath))
            .IsEquivalentTo(["vendor/lib"]);
        await Assert.That(preview.Unrecoverable[0].Loss.What).StartsWith("An external checkout:");
    }

    /// <summary>
    /// Measured on 1.8.15: the work queue fails part-way and <c>svn cleanup</c> then fails the same
    /// way until the obstruction is moved by hand.
    /// </summary>
    [Test]
    public async Task An_obstructed_target_is_refused()
    {
        var preview = Of(Clean("art/hero.png") with { Status = NodeStatus.Obstructed });

        await Assert
            .That(preview.Refusals)
            .IsEquivalentTo([
                "Something else is in art/hero.png's place on disk. Deleting it makes SVN stop "
                    + "part-way and leaves a cleanup that fails too; move what is there out of the way first.",
            ]);
    }

    [Test]
    public async Task A_target_the_app_offers_is_not_refused()
    {
        await Assert.That(Of(Clean("art/hero.png")).Refusals).IsEmpty();
    }

    /// <summary>
    /// Every refusal is the app's own sentence, so the two front-ends cannot explain one node two ways.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Unversioned)]
    [Arguments(NodeStatus.Ignored)]
    [Arguments(NodeStatus.External)]
    [Arguments(NodeStatus.Conflicted)]
    [Arguments(NodeStatus.Deleted)]
    [Arguments(NodeStatus.Incomplete)]
    public async Task A_target_the_app_refuses_is_refused_in_the_apps_words(NodeStatus status)
    {
        var target = Clean("art/hero.png") with { Status = status };

        await Assert
            .That(Of(target).Refusals)
            .IsEquivalentTo([DeletionOffer.RefusalFor(target, [target], Sensitive)!]);
    }

    [Test]
    public async Task The_working_copy_root_is_refused()
    {
        await Assert
            .That(Of(Folder(string.Empty)).Refusals)
            .IsEquivalentTo(["SVN cannot delete the root of a working copy."]);
    }

    /// <summary>
    /// The folder itself is fine to delete; what is beneath it is what the app refuses it for, so
    /// the whole listing has to reach the rule, not the target alone.
    /// </summary>
    [Test]
    public async Task A_folder_holding_a_half_updated_node_is_refused()
    {
        var preview = Of(
            Folder("art"),
            Clean("art/hero.png") with
            {
                Status = NodeStatus.Incomplete,
            }
        );

        await Assert
            .That(preview.Refusals)
            .IsEquivalentTo([
                "art/hero.png was left part-way through an update. Update it before deleting art.",
            ]);
    }

    [Test]
    public async Task Each_refused_target_is_named_even_when_another_is_fine()
    {
        var preview = RemovalPreview.Of(
            Status(
                Clean("art/hero.png") with
                {
                    Status = NodeStatus.Obstructed,
                },
                Clean("art/ok.png"),
                Clean("art/scratch.txt") with
                {
                    Status = NodeStatus.Unversioned,
                }
            ),
            [At("art/hero.png"), At("art/ok.png"), At("art/scratch.txt")],
            Sensitive
        );

        await Assert.That(preview.Refusals.Count).IsEqualTo(2);
        await Assert
            .That(preview.Refusals[0])
            .StartsWith("Something else is in art/hero.png's place");
        await Assert.That(preview.Refusals[1]).StartsWith("art/scratch.txt is not in SVN");
    }

    /// <summary>
    /// Windows compares paths without case, so a target spelled differently from the listing still
    /// finds its node there — and anywhere else it must not.
    /// </summary>
    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, 1)]
    [Arguments(StringComparison.Ordinal, 0)]
    public async Task A_target_finds_its_node_by_the_platforms_comparison(
        StringComparison comparison,
        int refusals
    )
    {
        var preview = RemovalPreview.Of(
            Status(Clean("art/hero.png") with { Status = NodeStatus.Obstructed }),
            [At("Art/Hero.png")],
            comparison
        );

        await Assert.That(preview.Refusals.Count).IsEqualTo(refusals);
    }

    [Test]
    public async Task A_target_the_listing_does_not_hold_is_neither_refused_nor_reached()
    {
        var preview = RemovalPreview.Of(Status(Clean("src/a.txt")), [At("art")], Sensitive);

        await Assert.That(preview.Refusals).IsEmpty();
        await Assert.That(preview.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The commonest removal of all, and once the broken one: a file nobody has edited is absent from
    /// a default listing, so the listing has to be asked for with both switches on.
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
    [Arguments(NodeStatus.Unversioned)]
    [Arguments(NodeStatus.Ignored)]
    public async Task A_file_svn_does_not_track_inside_a_removed_folder_is_unrecoverable(
        NodeStatus status
    )
    {
        var preview = Of(Folder("art"), Clean("art/scratch.txt") with { Status = status });

        await Assert
            .That(preview.Unrecoverable.Select(node => node.Entry.RelPath))
            .IsEquivalentTo(["art/scratch.txt"]);
    }

    /// <summary>A node already marked deleted is something the delete does nothing more to.</summary>
    [Test]
    public async Task A_node_already_deleted_inside_a_removed_folder_is_not_listed()
    {
        var preview = Of(Folder("art"), Clean("art/old.png") with { Status = NodeStatus.Deleted });

        await Assert.That(preview.Count).IsEqualTo(1);
        await Assert.That(preview.Recoverable[0].Entry.RelPath).IsEqualTo("art");
    }

    [Test]
    public async Task Only_the_nodes_under_the_named_target_are_listed()
    {
        var preview = RemovalPreview.Of(
            Status(Clean("art/hero.png"), Clean("src/a.txt")),
            [At("art")],
            Sensitive
        );

        await Assert
            .That(preview.Recoverable.Select(node => node.Entry.RelPath))
            .IsEquivalentTo(["art/hero.png"]);
    }

    [Test]
    public async Task A_node_two_targets_reach_is_listed_once()
    {
        var preview = RemovalPreview.Of(
            Status(Folder("art"), Clean("art/hero.png")),
            [At("art"), At("art/hero.png")],
            Sensitive
        );

        await Assert.That(preview.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Recoverable_nodes_are_shown_in_the_status_layout()
    {
        await Assert.That(Of(Clean("art/hero.png")).Lines).IsEquivalentTo(["        art/hero.png"]);
    }

    /// <summary>
    /// The warning only appears when there is something to warn about — printing it over an empty
    /// list is how people learn to skip it.
    /// </summary>
    [Test]
    public async Task Nothing_unrecoverable_prints_no_warning()
    {
        await Assert.That(Of(Folder("art"), Clean("art/hero.png")).Lines.Count).IsEqualTo(2);
    }

    [Test]
    public async Task The_unrecoverable_ones_are_called_out_below_each_with_what_it_loses()
    {
        var lines = Of(
            Folder("art"),
            Clean("art/new.png") with
            {
                Status = NodeStatus.Added,
            },
            Clean("art/one.txt") with
            {
                Status = NodeStatus.Unversioned,
            }
        ).Lines;

        await Assert
            .That(string.Join(NewLine, lines))
            .IsEqualTo(
                string.Join(
                    NewLine,
                    "        art",
                    string.Empty,
                    "2 of these lose work that cannot be brought back:",
                    "A       art/new.png: Deleted from disk; it was never committed, so nothing can bring it back",
                    "?       art/one.txt: Not in SVN: deleted from disk, and nothing can bring it back"
                )
            );
    }

    /// <summary>
    /// With nothing recoverable there is no list above to separate from, so the blank line would be
    /// a stray one at the top of the output.
    /// </summary>
    [Test]
    public async Task Only_unrecoverable_nodes_start_straight_at_the_warning_in_the_singular()
    {
        var lines = Of(Clean("art/new.png") with { Status = NodeStatus.Added }).Lines;

        await Assert
            .That(string.Join(NewLine, lines))
            .IsEqualTo(
                string.Join(
                    NewLine,
                    "1 of these loses work that cannot be brought back:",
                    "A       art/new.png: Deleted from disk; it was never committed, so nothing can bring it back"
                )
            );
    }

    private static RemovalPreview Of(params WorkingCopyEntry[] entries) =>
        RemovalPreview.Of(Status(entries), [At(entries[0].RelPath)], Sensitive);

    private static string At(string relPath) =>
        relPath.Length == 0
            ? Root
            : Path.Combine(Root, relPath.Replace('/', Path.DirectorySeparatorChar));

    private static StatusResponse Status(params WorkingCopyEntry[] entries) =>
        new(
            new WorkingCopyInfo(Root, "https://svn.example/repo", "uuid-1", 31),
            entries,
            ServedFromWarmIndex: true,
            ServerElapsedMilliseconds: 0,
            UnfinishedOperations: 0,
            UnrecordedMoves: []
        );

    private static WorkingCopyEntry Clean(string relPath) =>
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

    private static WorkingCopyEntry Folder(string relPath) =>
        Clean(relPath) with
        {
            Kind = NodeKind.Directory,
        };
}
