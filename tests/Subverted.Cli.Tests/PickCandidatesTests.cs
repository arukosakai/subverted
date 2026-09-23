using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// What the picker is allowed to offer. Two things ride on it: offering a node <c>svn commit</c>
/// refuses fails the whole commit, and the order decides whether a directory is answered before the
/// children its answer settles.
/// </summary>
public sealed class PickCandidatesTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    private static readonly string Root = Path.GetFullPath("/wc");

    [Test]
    [Arguments(NodeStatus.Modified)]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Deleted)]
    [Arguments(NodeStatus.Replaced)]
    [Arguments(NodeStatus.NeedsPristineCompare)]
    [Arguments(NodeStatus.Unversioned)]
    [Arguments(NodeStatus.Missing)]
    public async Task A_node_a_selection_commit_can_take_is_offered(NodeStatus status)
    {
        var listing = Status(Modified("src/a.txt") with { Status = status });

        await Assert.That(Under(listing).Select(entry => entry.RelPath)).Contains("src/a.txt");
    }

    /// <summary>
    /// The negative half of the case above, and the one that matters more: the daemon refuses every
    /// one of these, and one refusal stops the whole picked set.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Unmodified)]
    [Arguments(NodeStatus.Ignored)]
    [Arguments(NodeStatus.External)]
    [Arguments(NodeStatus.Obstructed)]
    [Arguments(NodeStatus.Incomplete)]
    [Arguments(NodeStatus.Conflicted)]
    public async Task A_node_svn_cannot_commit_as_it_stands_is_not_offered(NodeStatus status)
    {
        var listing = Status(Modified("src/a.txt") with { Status = status });

        await Assert.That(Under(listing)).IsEmpty();
    }

    /// <summary>
    /// A node in conflict is refused by <c>svn commit</c> however its content axis reads, so the
    /// content status alone is not enough to decide this.
    /// </summary>
    [Test]
    public async Task A_conflicted_node_is_not_offered_even_when_its_content_reads_modified()
    {
        var listing = Status(Modified("src/a.txt") with { IsConflicted = true });

        await Assert.That(Under(listing)).IsEmpty();
    }

    /// <summary>
    /// SVN's second column. A property-only change leaves the content axis reading clean, and a
    /// picker that trusted that axis alone would never let anyone commit one.
    /// </summary>
    [Test]
    public async Task A_property_only_change_is_offered_although_its_content_is_unmodified()
    {
        var listing = Status(PropertiesChanged("src"));

        await Assert.That(Under(listing).Select(entry => entry.RelPath)).IsEquivalentTo(["src"]);
    }

    [Test]
    public async Task Only_what_is_under_the_named_path_is_offered()
    {
        var listing = Status(Modified("art/hero.png"), Modified("src/a.txt"));

        var candidates = PickCandidates.Under(listing, Path.Combine(Root, "art"), Sensitive);

        await Assert
            .That(candidates.Select(entry => entry.RelPath))
            .IsEquivalentTo(["art/hero.png"]);
    }

    /// <summary>
    /// Ancestors first, because every directory rule in <see cref="ChangePicker"/> depends on the
    /// directory having been answered before the nodes its answer settles. Ordinal order gives it
    /// for free — <c>/</c> sorts below every character a name can start with.
    /// </summary>
    [Test]
    public async Task A_directory_is_offered_before_anything_under_it_however_the_daemon_listed_them()
    {
        var listing = Status(
            Modified("src/a.txt"),
            Modified("srcfile.txt"),
            PropertiesChanged("src"),
            Modified("src/deep/d.txt"),
            PropertiesChanged("src/deep")
        );

        var candidates = PickCandidates.Under(listing, Root, Sensitive);

        await Assert
            .That(candidates.Select(entry => entry.RelPath))
            .IsEquivalentTo(["src", "src/a.txt", "src/deep", "src/deep/d.txt", "srcfile.txt"]);
    }

    [Test]
    public async Task Nothing_committable_under_the_path_is_an_empty_list()
    {
        var listing = Status(Modified("art/hero.png"));

        await Assert
            .That(PickCandidates.Under(listing, Path.Combine(Root, "src"), Sensitive))
            .IsEmpty();
    }

    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, 1)]
    [Arguments(StringComparison.Ordinal, 0)]
    public async Task Case_is_the_platforms_business_and_not_this_functions(
        StringComparison comparison,
        int expected
    )
    {
        var listing = Status(Modified("Art/hero.png"));

        var candidates = PickCandidates.Under(listing, Path.Combine(Root, "art"), comparison);

        await Assert.That(candidates.Count).IsEqualTo(expected);
    }

    /// <summary>
    /// A rename made outside SVN is one question. Offered as its two halves, a yes to one and a no
    /// to the other is a set the daemon refuses, and both as plain nodes would end the history.
    /// </summary>
    [Test]
    public async Task A_rename_made_outside_svn_is_offered_once_as_both_its_halves()
    {
        var listing = Renamed(
            ("art/hero.png", "art/protagonist.png"),
            Missing("art/hero.png"),
            Unversioned("art/protagonist.png"),
            Modified("art/villain.png")
        );

        var candidates = Under(listing);

        await Assert
            .That(candidates.Select(candidate => candidate.RelPath))
            .IsEquivalentTo(["art/protagonist.png", "art/villain.png"]);
        await Assert.That(candidates[0].RenamedFrom?.RelPath).IsEqualTo("art/hero.png");
        await Assert
            .That(candidates[0].RelPaths)
            .IsEquivalentTo(["art/hero.png", "art/protagonist.png"]);
        await Assert.That(candidates[1].RenamedFrom).IsNull();
        await Assert.That(candidates[1].RelPaths).IsEquivalentTo(["art/villain.png"]);
    }

    /// <summary>
    /// A rename whose other half is outside the named path would need a path the user did not
    /// name, and either half alone is refused — so neither is offered.
    /// </summary>
    [Test]
    [Arguments("art/hero.png", "src/protagonist.png")]
    [Arguments("src/hero.png", "art/protagonist.png")]
    public async Task A_rename_the_named_path_cuts_in_two_is_not_offered_at_all(
        string from,
        string to
    )
    {
        var listing = Renamed((from, to), Missing(from), Unversioned(to));

        await Assert
            .That(PickCandidates.Under(listing, Path.Combine(Root, "art"), Sensitive))
            .IsEmpty();
    }

    /// <summary>A pair the listing no longer holds both halves of is not offered as a rename.</summary>
    [Test]
    public async Task A_pair_missing_one_of_its_entries_is_not_offered()
    {
        var listing = Renamed(
            ("art/hero.png", "art/protagonist.png"),
            Unversioned("art/protagonist.png")
        );

        await Assert.That(Under(listing)).IsEmpty();
    }

    [Test]
    public async Task A_pair_missing_its_new_half_is_not_offered_either()
    {
        var listing = Renamed(("art/hero.png", "art/protagonist.png"), Missing("art/hero.png"));

        await Assert.That(Under(listing)).IsEmpty();
    }

    [Test]
    [Arguments(NodeStatus.Unversioned, true)]
    [Arguments(NodeStatus.Missing, false)]
    [Arguments(NodeStatus.Modified, false)]
    public async Task Only_an_unversioned_node_on_its_own_is_new(NodeStatus status, bool isNew)
    {
        var listing = Status(Modified("src/a.txt") with { Status = status });

        await Assert.That(Under(listing)[0].IsNew).IsEqualTo(isNew);
    }

    [Test]
    public async Task The_new_half_of_a_rename_is_not_new_because_it_is_moved_rather_than_added()
    {
        var listing = Renamed(
            ("art/hero.png", "art/protagonist.png"),
            Missing("art/hero.png"),
            Unversioned("art/protagonist.png")
        );

        await Assert.That(Under(listing)[0].IsNew).IsFalse();
    }

    private static StatusResponse Renamed(
        (string From, string To) move,
        params WorkingCopyEntry[] entries
    ) => Status(entries) with { UnrecordedMoves = [new UnrecordedMove(move.From, move.To)] };

    private static WorkingCopyEntry Missing(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Missing,
        };

    private static WorkingCopyEntry Unversioned(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Unversioned,
            Revision = null,
        };

    private static IReadOnlyList<PickCandidate> Under(StatusResponse listing) =>
        PickCandidates.Under(listing, Root, Sensitive);

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

    private static WorkingCopyEntry PropertiesChanged(string relPath) =>
        Modified(relPath) with
        {
            Kind = NodeKind.Directory,
            Status = NodeStatus.Unmodified,
            PropertyStatus = PropertyStatus.Modified,
        };
}
