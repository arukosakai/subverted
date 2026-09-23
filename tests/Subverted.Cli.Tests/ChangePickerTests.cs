using Subverted.Core;

namespace Subverted.Cli.Tests;

/// <summary>
/// The walk that decides what goes to the server. Every rule is tested in both directions, because
/// each one fails two ways: a node wrongly kept is work that silently did not ship, and a node
/// wrongly picked is work somebody declined and got anyway.
/// </summary>
/// <remarks>
/// The directory rules come from <c>svn</c> 1.8.15 and are pinned against the real client in
/// <c>SvnWriteIntegrationTests</c>. These tests state what the picker does with them; those state
/// that SVN does what the picker believes.
/// </remarks>
public sealed class ChangePickerTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    [Test]
    public async Task Yes_sends_the_node_and_no_leaves_it_local()
    {
        var picker = Walk([File("src/a.txt"), File("src/b.txt")], PickAnswer.Send, PickAnswer.Skip);

        await Assert.That(Picked(picker)).IsEquivalentTo(["src/a.txt"]);
        await Assert.That(picker.Abandoned).IsFalse();
    }

    [Test]
    public async Task The_walk_ends_once_every_node_has_been_answered()
    {
        var picker = Walk([File("src/a.txt")], PickAnswer.Send);

        await Assert.That(picker.Current).IsNull();
    }

    [Test]
    public async Task Each_node_is_offered_in_turn_until_there_are_none_left()
    {
        var picker = new ChangePicker(Of([File("src/a.txt"), File("src/b.txt")]), Sensitive);

        await Assert.That(picker.Current?.RelPath).IsEqualTo("src/a.txt");
        picker.Answer(PickAnswer.Skip);
        await Assert.That(picker.Current?.RelPath).IsEqualTo("src/b.txt");
        picker.Answer(PickAnswer.Skip);
        await Assert.That(picker.Current).IsNull();
    }

    [Test]
    public async Task All_sends_this_node_and_every_one_left_without_asking_again()
    {
        var picker = Walk(
            [File("src/a.txt"), File("src/b.txt"), File("src/c.txt")],
            PickAnswer.Skip,
            PickAnswer.SendRest
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["src/b.txt", "src/c.txt"]);
        await Assert.That(picker.Current).IsNull();
    }

    [Test]
    public async Task Done_stops_asking_and_keeps_what_was_already_picked()
    {
        var picker = Walk(
            [File("src/a.txt"), File("src/b.txt"), File("src/c.txt")],
            PickAnswer.Send,
            PickAnswer.SkipRest
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["src/a.txt"]);
        await Assert.That(picker.Current).IsNull();
        await Assert.That(picker.Abandoned).IsFalse();
    }

    /// <summary>
    /// Quitting is not the same as picking nothing: it throws away answers already given, which is
    /// the whole reason to offer it separately from <c>d</c>.
    /// </summary>
    [Test]
    public async Task Quit_discards_what_was_already_picked_and_says_so()
    {
        var picker = Walk([File("src/a.txt"), File("src/b.txt")], PickAnswer.Send, PickAnswer.Quit);

        await Assert.That(picker.Picked).IsEmpty();
        await Assert.That(picker.Abandoned).IsTrue();
        await Assert.That(picker.Current).IsNull();
    }

    [Test]
    public async Task Asking_what_the_letters_mean_answers_nothing_and_offers_the_same_node_again()
    {
        var picker = new ChangePicker(Of([File("src/a.txt")]), Sensitive);

        picker.Answer(PickAnswer.Explain);

        await Assert.That(picker.Current?.RelPath).IsEqualTo("src/a.txt");
        await Assert.That(picker.Picked).IsEmpty();
        await Assert.That(picker.Abandoned).IsFalse();
    }

    /// <summary>
    /// SVN refuses a commit whose child has an added parent outside it, so declining the directory
    /// takes the choice away rather than leaving one that fails.
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Replaced)]
    public async Task Nothing_below_a_declined_added_directory_is_offered(NodeStatus status)
    {
        var picker = Walk(
            [Directory("fresh", status), Added("fresh/n.txt"), Added("fresh/m.txt")],
            PickAnswer.Skip
        );

        await Assert.That(picker.Picked).IsEmpty();
        await Assert.That(Decided(picker)).IsEquivalentTo(["fresh/n.txt", "fresh/m.txt"]);
    }

    /// <summary>
    /// The other half. Once the directory is going, its children are a real choice again — which is
    /// the case that makes the rule a rule rather than "never offer children".
    /// </summary>
    [Test]
    [Arguments(NodeStatus.Added)]
    [Arguments(NodeStatus.Replaced)]
    public async Task Below_an_accepted_added_directory_each_child_is_still_asked_about(
        NodeStatus status
    )
    {
        var picker = Walk(
            [Directory("fresh", status), Added("fresh/n.txt"), Added("fresh/m.txt")],
            PickAnswer.Send,
            PickAnswer.Send,
            PickAnswer.Skip
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["fresh", "fresh/n.txt"]);
        await Assert.That(picker.DecidedByAnAncestor).IsEmpty();
    }

    /// <summary>
    /// A deletion is recorded on the directory: naming it removes the subtree, and naming a child
    /// alone sends nothing. So the children follow the directory's answer either way.
    /// </summary>
    [Test]
    [Arguments(PickAnswer.Send)]
    [Arguments(PickAnswer.Skip)]
    public async Task A_deleted_directorys_children_follow_it_whichever_way_it_is_answered(
        PickAnswer answer
    )
    {
        var picker = Walk([Directory("art", NodeStatus.Deleted), Deleted("art/hero.png")], answer);

        await Assert.That(Decided(picker)).IsEquivalentTo(["art/hero.png"]);
        await Assert.That(picker.Current).IsNull();
    }

    /// <summary>
    /// A replaced directory carries away the deletions of what it replaced, and leaves what is
    /// newly added under it still to be decided. Both arms of that are here.
    /// </summary>
    [Test]
    public async Task A_replaced_directory_carries_its_deletions_and_still_asks_about_its_additions()
    {
        var picker = Walk(
            [Directory("lib", NodeStatus.Replaced), Deleted("lib/old.txt"), Added("lib/new.txt")],
            PickAnswer.Send,
            PickAnswer.Send
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["lib", "lib/new.txt"]);
        await Assert.That(Decided(picker)).IsEquivalentTo(["lib/old.txt"]);
    }

    /// <summary>
    /// A directory that only changed its properties settles nothing below it — the rule is about
    /// added and deleted directories, not about directories.
    /// </summary>
    [Test]
    public async Task A_directory_whose_properties_alone_changed_settles_nothing_below_it()
    {
        var picker = Walk(
            [PropertiesChanged("src"), File("src/a.txt")],
            PickAnswer.Skip,
            PickAnswer.Send
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["src/a.txt"]);
        await Assert.That(picker.DecidedByAnAncestor).IsEmpty();
    }

    /// <summary>A deleted <em>file</em> is a node like any other; only directories carry a subtree.</summary>
    [Test]
    public async Task A_deleted_file_is_offered_like_anything_else()
    {
        var picker = Walk([Deleted("src/a.txt")], PickAnswer.Send);

        await Assert.That(Picked(picker)).IsEquivalentTo(["src/a.txt"]);
    }

    [Test]
    public async Task The_rule_reaches_every_level_below_the_directory_and_not_only_its_children()
    {
        var picker = Walk(
            [Directory("fresh", NodeStatus.Added), Added("fresh/deep"), Added("fresh/deep/d.txt")],
            PickAnswer.Skip
        );

        await Assert.That(Decided(picker)).IsEquivalentTo(["fresh/deep", "fresh/deep/d.txt"]);
    }

    /// <summary>
    /// The sibling that shares a prefix. Declining <c>art</c> must not take <c>artefacts</c> with
    /// it, which a plain <c>StartsWith</c> on the name would.
    /// </summary>
    [Test]
    public async Task A_sibling_whose_name_starts_with_the_directorys_is_still_offered()
    {
        var picker = Walk(
            [Directory("art", NodeStatus.Added), Added("artefacts/notes.txt")],
            PickAnswer.Skip,
            PickAnswer.Send
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["artefacts/notes.txt"]);
        await Assert.That(picker.DecidedByAnAncestor).IsEmpty();
    }

    /// <summary>
    /// <c>a</c> means "every one left that is still a choice". Sweeping up the children of a
    /// directory that was already declined would build a set SVN refuses outright.
    /// </summary>
    [Test]
    public async Task All_does_not_sweep_up_what_a_declined_directory_already_ruled_out()
    {
        var picker = Walk(
            [Directory("fresh", NodeStatus.Added), Added("fresh/n.txt"), Modified("src/a.txt")],
            PickAnswer.Skip,
            PickAnswer.SendRest
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["src/a.txt"]);
        await Assert.That(Decided(picker)).IsEquivalentTo(["fresh/n.txt"]);
    }

    /// <summary>
    /// <c>a</c> on a deleted directory has the same reach its explicit answer would: the subtree
    /// goes with it and is not listed again as though it were sent separately.
    /// </summary>
    [Test]
    public async Task All_on_a_deleted_directory_still_lets_it_carry_its_own_subtree()
    {
        var picker = Walk(
            [Directory("art", NodeStatus.Deleted), Deleted("art/hero.png"), Modified("src/a.txt")],
            PickAnswer.SendRest
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["art", "src/a.txt"]);
        await Assert.That(Decided(picker)).IsEquivalentTo(["art/hero.png"]);
    }

    [Test]
    [Arguments(StringComparison.OrdinalIgnoreCase, 0)]
    [Arguments(StringComparison.Ordinal, 1)]
    public async Task Case_is_the_platforms_business_and_not_this_walks(
        StringComparison comparison,
        int expectedOffers
    )
    {
        var picker = new ChangePicker(
            Of([Directory("fresh", NodeStatus.Added), Added("FRESH/n.txt")]),
            comparison
        );

        picker.Answer(PickAnswer.Skip);

        await Assert.That(picker.Current is null ? 0 : 1).IsEqualTo(expectedOffers);
    }

    [Test]
    public async Task Nothing_to_pick_is_an_empty_walk_rather_than_an_abandoned_one()
    {
        var picker = new ChangePicker(Of([]), Sensitive);

        await Assert.That(picker.Current).IsNull();
        await Assert.That(picker.Picked).IsEmpty();
        await Assert.That(picker.Abandoned).IsFalse();
    }

    [Test]
    public async Task Answering_when_there_is_nothing_left_to_answer_is_refused()
    {
        var picker = Walk([File("src/a.txt")], PickAnswer.Send);

        await Assert.That(() => picker.Answer(PickAnswer.Send)).Throws<InvalidOperationException>();
    }

    /// <summary>
    /// <see cref="PickAnswers"/> never produces one of these, but an enum holds any number and a
    /// silent fall-through to "skip" would drop a node the person said yes to.
    /// </summary>
    [Test]
    public async Task An_answer_the_walk_does_not_know_is_refused_rather_than_assumed()
    {
        var picker = new ChangePicker(Of([File("src/a.txt")]), Sensitive);

        await Assert
            .That(() => picker.Answer((PickAnswer)(-1)))
            .Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// A rename is one answer. Yes sends both halves together, which is the only way the daemon
    /// will record it as a move.
    /// </summary>
    [Test]
    public async Task Yes_to_a_rename_sends_both_of_its_halves()
    {
        var picker = new ChangePicker([Rename("art/hero.png", "art/protagonist.png")], Sensitive);

        picker.Answer(PickAnswer.Send);

        await Assert
            .That(picker.Picked.SelectMany(candidate => candidate.RelPaths))
            .IsEquivalentTo(["art/hero.png", "art/protagonist.png"]);
    }

    [Test]
    public async Task No_to_a_rename_sends_neither_half()
    {
        var picker = new ChangePicker([Rename("art/hero.png", "art/protagonist.png")], Sensitive);

        picker.Answer(PickAnswer.Skip);

        await Assert.That(picker.Picked).IsEmpty();
        await Assert.That(picker.Current).IsNull();
    }

    /// <summary>
    /// A rename whose new path sits in a declined added directory cannot go: the daemon would be
    /// asked to move a node into a folder the server has never seen.
    /// </summary>
    [Test]
    public async Task A_rename_whose_new_half_a_declined_directory_ruled_out_is_not_offered()
    {
        var picker = new ChangePicker(
            [
                new PickCandidate(Directory("fresh", NodeStatus.Added)),
                Rename("other/old.png", "fresh/new.png"),
            ],
            Sensitive
        );

        picker.Answer(PickAnswer.Skip);

        await Assert.That(picker.Current).IsNull();
    }

    /// <summary>
    /// <c>a</c> sends what the GUI ticks by default (edits, deletions, renames) and leaves new
    /// nodes, because an unversioned file is as often build output as work.
    /// </summary>
    [Test]
    public async Task All_sends_the_rest_but_leaves_new_nodes_unversioned_and_says_which()
    {
        var picker = new ChangePicker(
            [
                new PickCandidate(Modified("a.txt")),
                new PickCandidate(Unversioned("build.log")),
                new PickCandidate(Missing("gone.txt")),
                Rename("old.png", "new.png"),
            ],
            Sensitive
        );

        picker.Answer(PickAnswer.SendRest);

        await Assert.That(Picked(picker)).IsEquivalentTo(["a.txt", "gone.txt", "new.png"]);
        await Assert
            .That(picker.NewLeftBySendRest.Select(candidate => candidate.RelPath))
            .IsEquivalentTo(["build.log"]);
    }

    /// <summary><c>a</c> on a new node is an answer for that node, so it is added.</summary>
    [Test]
    public async Task All_on_a_new_node_sends_that_node()
    {
        var picker = new ChangePicker(
            [new PickCandidate(Unversioned("new.txt")), new PickCandidate(Unversioned("junk.txt"))],
            Sensitive
        );

        picker.Answer(PickAnswer.SendRest);

        await Assert.That(Picked(picker)).IsEquivalentTo(["new.txt"]);
        await Assert
            .That(picker.NewLeftBySendRest.Select(candidate => candidate.RelPath))
            .IsEquivalentTo(["junk.txt"]);
    }

    [Test]
    public async Task Yes_to_a_new_node_sends_it()
    {
        var picker = Walk([Unversioned("new.txt")], PickAnswer.Send);

        await Assert.That(Picked(picker)).IsEquivalentTo(["new.txt"]);
        await Assert.That(picker.NewLeftBySendRest).IsEmpty();
    }

    /// <summary>
    /// Measured: <c>svn delete</c> on a missing directory records its whole missing subtree, so
    /// once it is sent the children are not a choice.
    /// </summary>
    [Test]
    public async Task A_sent_missing_directory_takes_its_missing_subtree_with_it()
    {
        var picker = Walk(
            [
                Directory("gone", NodeStatus.Missing),
                Missing("gone/a.txt"),
                Directory("gone/deep", NodeStatus.Missing),
                Missing("gone/deep/b.txt"),
            ],
            PickAnswer.Send
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["gone"]);
        await Assert
            .That(Decided(picker))
            .IsEquivalentTo(["gone/a.txt", "gone/deep", "gone/deep/b.txt"]);
        await Assert.That(picker.Current).IsNull();
    }

    /// <summary>
    /// Measured too: with the directory left, a missing child is deleted and committed on its
    /// own, so it is still a real choice.
    /// </summary>
    [Test]
    public async Task Below_a_declined_missing_directory_each_missing_child_is_still_asked_about()
    {
        var picker = Walk(
            [Directory("gone", NodeStatus.Missing), Missing("gone/a.txt")],
            PickAnswer.Skip,
            PickAnswer.Send
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["gone/a.txt"]);
        await Assert.That(picker.DecidedByAnAncestor).IsEmpty();
    }

    [Test]
    public async Task A_sent_missing_directory_settles_nothing_that_shares_its_name_as_a_prefix()
    {
        var picker = Walk(
            [Directory("gone", NodeStatus.Missing), Missing("gonebut/a.txt")],
            PickAnswer.Send,
            PickAnswer.Skip
        );

        await Assert.That(Picked(picker)).IsEquivalentTo(["gone"]);
        await Assert.That(picker.DecidedByAnAncestor).IsEmpty();
    }

    private static ChangePicker Walk(
        IReadOnlyList<WorkingCopyEntry> candidates,
        params PickAnswer[] answers
    )
    {
        var picker = new ChangePicker(Of(candidates), Sensitive);
        foreach (var answer in answers)
        {
            picker.Answer(answer);
        }

        return picker;
    }

    private static IEnumerable<string> Picked(ChangePicker picker) =>
        picker.Picked.Select(entry => entry.RelPath);

    private static IEnumerable<string> Decided(ChangePicker picker) =>
        picker.DecidedByAnAncestor.Select(entry => entry.RelPath);

    private static WorkingCopyEntry File(string relPath) => Modified(relPath);

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

    private static WorkingCopyEntry Added(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Added,
        };

    private static WorkingCopyEntry Deleted(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Deleted,
        };

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

    private static PickCandidate Rename(string from, string to) =>
        new(Unversioned(to), RenamedFrom: Missing(from));

    private static WorkingCopyEntry Directory(string relPath, NodeStatus status) =>
        Modified(relPath) with
        {
            Kind = NodeKind.Directory,
            Status = status,
        };

    private static WorkingCopyEntry PropertiesChanged(string relPath) =>
        Directory(relPath, NodeStatus.Unmodified) with
        {
            PropertyStatus = PropertyStatus.Modified,
        };

    private static IReadOnlyList<PickCandidate> Of(IReadOnlyList<WorkingCopyEntry> entries) =>
        [.. entries.Select(entry => new PickCandidate(entry))];
}
