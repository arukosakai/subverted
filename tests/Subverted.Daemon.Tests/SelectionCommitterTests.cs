using Subverted.Core;
using Subverted.Protocol;
using Subverted.Svn;
using TUnit.Assertions.Enums;

namespace Subverted.Daemon.Tests;

/// <summary>
/// The order of the steps, what each hands the client, and what a failure at each one reports.
/// The client is a recording fake; what SVN really does with the same calls is in
/// <see cref="CommitSelectionEndToEndTests"/>.
/// </summary>
public sealed class SelectionCommitterTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string Root = Path.GetFullPath("/wc");

    private static readonly UnrecordedMove HeroRenamed = new("art/hero.png", "art/protagonist.png");

    [Test]
    public async Task A_refused_plan_writes_nothing_and_comes_back_as_an_error()
    {
        var world = new World([Entry("x", NodeStatus.Ignored)]);

        var response = await world.CommitAsync("x");

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert.That(((ErrorResponse)response).Kind).IsEqualTo(DaemonErrorKind.RequestRefused);
        await Assert
            .That(((ErrorResponse)response).Message)
            .IsEqualTo("'x' is ignored. If it belongs in the repository, add it by hand first.");
        await Assert.That(world.Calls).IsEmpty();
    }

    [Test]
    public async Task Every_refusal_is_in_the_message_one_per_line()
    {
        var world = new World([Entry("x", NodeStatus.Ignored), Entry("y", NodeStatus.External)]);

        var response = (ErrorResponse)await world.CommitAsync("x", "y");

        await Assert
            .That(response.Message.Split(Environment.NewLine))
            .IsEquivalentTo(
                [
                    "'x' is ignored. If it belongs in the repository, add it by hand first.",
                    "'y' is an svn:externals checkout, a working copy of its own. Commit it from "
                        + "there.",
                ],
                CollectionOrdering.Matching
            );
    }

    /// <summary>
    /// Moves before additions: a rename's route is read off disk, and it has to find the file where
    /// the person left it. The commit comes last and names every ticked node, both move halves too.
    /// </summary>
    [Test]
    public async Task A_mixed_selection_moves_then_adds_then_deletes_then_commits_exactly_those_nodes()
    {
        var world = new World(
            [
                Entry("readme.txt", NodeStatus.Modified),
                Entry("art/new.png", NodeStatus.Unversioned),
                Entry("art/old.png", NodeStatus.Missing),
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("art/protagonist.png", NodeStatus.Unversioned),
            ],
            [HeroRenamed]
        );

        var response = await world.CommitAsync(
            "readme.txt",
            "art/new.png",
            "art/old.png",
            "art/hero.png",
            "art/protagonist.png"
        );

        await Assert
            .That(world.Calls)
            .IsEquivalentTo(
                [
                    $"move {At("art/hero.png")} -> {At("art/protagonist.png")}",
                    $"add {At("art/new.png")}",
                    $"delete {At("art/old.png")}",
                    $"commit ExactlyTheseNodes {At("readme.txt")} {At("art/new.png")} "
                        + $"{At("art/old.png")} {At("art/hero.png")} {At("art/protagonist.png")}",
                ],
                CollectionOrdering.Matching
            );

        await Assert.That(response).IsTypeOf<CommitSelectionResponse>();
        var committed = (CommitSelectionResponse)response;
        await Assert.That(committed.Revision).IsEqualTo(7L);
        await Assert.That(committed.Scheduled.Added).IsEquivalentTo(["art/new.png"]);
        await Assert.That(committed.Scheduled.Deleted).IsEquivalentTo(["art/old.png"]);
        await Assert
            .That(committed.Scheduled.Moved)
            .IsEquivalentTo([new RecordedMove("art/hero.png", "art/protagonist.png")]);
        await Assert.That(committed.Notifications).IsEqualTo("moved\nadded\ndeleted\ncommitted\n");
    }

    [Test]
    public async Task An_edit_alone_runs_nothing_but_the_commit()
    {
        var world = new World([Entry("readme.txt", NodeStatus.Modified)]);

        await world.CommitAsync("readme.txt");

        await Assert
            .That(world.Calls)
            .IsEquivalentTo([$"commit ExactlyTheseNodes {At("readme.txt")}"]);
    }

    [Test]
    public async Task Nothing_to_send_is_still_a_success_with_no_revision()
    {
        var world = new World([Entry("readme.txt", NodeStatus.Unmodified)]) { Revision = null };

        var response = await world.CommitAsync("readme.txt");

        await Assert.That(response).IsTypeOf<CommitSelectionResponse>();
        await Assert.That(((CommitSelectionResponse)response).Revision).IsNull();
    }

    /// <summary>
    /// <c>svn add</c> recurses and skips what is ignored; the commit at <c>--depth empty</c> takes
    /// only what it names. So the committer asks again after adding, and names what arrived beneath
    /// the directory — and nothing else, ignored or elsewhere.
    /// </summary>
    [Test]
    public async Task An_added_directory_commits_everything_the_add_scheduled_beneath_it()
    {
        var world = new World([
            Entry("newdir", NodeStatus.Unversioned, NodeKind.Directory),
            Entry("elsewhere/earlier.txt", NodeStatus.Added),
        ]);
        world.AfterAdding =
        [
            Entry("newdir", NodeStatus.Added, NodeKind.Directory),
            Entry("newdir/b.txt", NodeStatus.Added),
            Entry("newdir/a.txt", NodeStatus.Added),
            Entry("newdir/junk.obj", NodeStatus.Ignored),
            Entry("newdirectory.txt", NodeStatus.Added),
            Entry("elsewhere/earlier.txt", NodeStatus.Added),
        ];

        var response = (CommitSelectionResponse)await world.CommitAsync("newdir");

        await Assert
            .That(world.Calls.Last())
            .IsEqualTo(
                $"commit ExactlyTheseNodes {At("newdir")} {At("newdir/a.txt")} {At("newdir/b.txt")}"
            );
        await Assert
            .That(response.Scheduled.Added)
            .IsEquivalentTo(
                ["newdir", "newdir/a.txt", "newdir/b.txt"],
                CollectionOrdering.Matching
            );
    }

    /// <summary>A file added needs no second look, and on a large tree the look is a scan.</summary>
    [Test]
    public async Task An_added_file_does_not_make_the_committer_read_the_working_copy_again()
    {
        var world = new World([Entry("new.txt", NodeStatus.Unversioned)]);

        await world.CommitAsync("new.txt");

        await Assert.That(world.Scan.Scans).IsEqualTo(1);
    }

    [Test]
    public async Task A_rename_that_fails_in_the_client_stops_before_anything_else_runs()
    {
        var world = TheMixedWorld();
        world.Failing = "move";

        var response = await world.CommitAsync(TheMixedSelection);

        var stopped = await StoppedAt(response, SelectionStep.Move);
        await Assert.That(stopped.Scheduled.Moved).IsEmpty();
        await Assert.That(world.Calls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task An_addition_that_fails_leaves_the_recorded_move_standing_and_says_so()
    {
        var world = TheMixedWorld();
        world.Failing = "add";

        var stopped = await StoppedAt(
            await world.CommitAsync(TheMixedSelection),
            SelectionStep.Addition
        );

        await Assert
            .That(stopped.Scheduled.Moved)
            .IsEquivalentTo([new RecordedMove("art/hero.png", "art/protagonist.png")]);
        await Assert.That(stopped.Scheduled.Added).IsEmpty();
        await Assert.That(stopped.Notifications).IsEqualTo("moved\n");
        await Assert.That(world.Calls.Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_deletion_that_fails_leaves_the_move_and_the_addition_standing()
    {
        var world = TheMixedWorld();
        world.Failing = "delete";

        var stopped = await StoppedAt(
            await world.CommitAsync(TheMixedSelection),
            SelectionStep.Deletion
        );

        await Assert.That(stopped.Scheduled.Moved.Count).IsEqualTo(1);
        await Assert.That(stopped.Scheduled.Added).IsEquivalentTo(["art/new.png"]);
        await Assert.That(stopped.Scheduled.Deleted).IsEmpty();
        await Assert.That(world.Calls.Count).IsEqualTo(3);
    }

    /// <summary>
    /// The case slice 3 names: the server refused after every mark was made. Nothing is rolled
    /// back, the response lists all of it, and SVN's own words say why.
    /// </summary>
    [Test]
    public async Task A_commit_that_fails_after_scheduling_leaves_all_of_it_and_carries_svns_text()
    {
        var world = TheMixedWorld();
        world.Failing = "commit";

        var stopped = await StoppedAt(
            await world.CommitAsync(TheMixedSelection),
            SelectionStep.Commit
        );

        await Assert.That(stopped.Scheduled.Moved.Count).IsEqualTo(1);
        await Assert.That(stopped.Scheduled.Added).IsEquivalentTo(["art/new.png"]);
        await Assert.That(stopped.Scheduled.Deleted).IsEquivalentTo(["art/old.png"]);
        await Assert.That(stopped.Notifications).IsEqualTo("moved\nadded\ndeleted\n");
        await Assert.That(stopped.Failure).IsEqualTo("svn: the commit failed");
        await Assert.That(world.Calls.Count).IsEqualTo(4);
    }

    /// <summary>
    /// A refusing route runs no client. As the first thing to happen that means nothing was
    /// written, which is what an error response promises.
    /// </summary>
    [Test]
    public async Task A_rename_refused_before_anything_was_written_is_an_error()
    {
        var world = TheMixedWorld();
        world.Route = MoveRoute.DestinationOccupied;

        var response = await world.CommitAsync(TheMixedSelection);

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert.That(((ErrorResponse)response).Kind).IsEqualTo(DaemonErrorKind.RequestRefused);
        await Assert
            .That(((ErrorResponse)response).Message)
            .IsEqualTo(
                MoveRefusal.Explain(
                    MoveRoute.DestinationOccupied,
                    At("art/hero.png"),
                    At("art/protagonist.png")
                )
            );
    }

    /// <summary>After one rename was recorded, a refused second one is no longer "nothing happened".</summary>
    [Test]
    public async Task A_rename_refused_after_another_was_recorded_reports_the_one_that_stands()
    {
        var villain = new UnrecordedMove("art/villain.png", "art/antagonist.png");
        var world = new World(
            [
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("art/protagonist.png", NodeStatus.Unversioned),
                Entry("art/villain.png", NodeStatus.Missing),
                Entry("art/antagonist.png", NodeStatus.Unversioned),
            ],
            [HeroRenamed, villain]
        );
        world.RouteFor = source =>
            source == At("art/villain.png") ? MoveRoute.NothingAtSource : MoveRoute.AlreadyRenamed;

        var stopped = await StoppedAt(
            await world.CommitAsync(
                "art/hero.png",
                "art/protagonist.png",
                "art/villain.png",
                "art/antagonist.png"
            ),
            SelectionStep.Move
        );

        await Assert
            .That(stopped.Scheduled.Moved)
            .IsEquivalentTo([new RecordedMove("art/hero.png", "art/protagonist.png")]);
        await Assert
            .That(stopped.Failure)
            .IsEqualTo(
                MoveRefusal.Explain(
                    MoveRoute.NothingAtSource,
                    At("art/villain.png"),
                    At("art/antagonist.png")
                )
            );
    }

    /// <summary>
    /// The adds have run by the time the second look fails, so this cannot be an error response —
    /// that would tell the front-end nothing was written.
    /// </summary>
    [Test]
    public async Task Failing_to_read_what_an_added_directory_scheduled_still_reports_the_addition_step()
    {
        var world = new World([Entry("newdir", NodeStatus.Unversioned, NodeKind.Directory)]);
        world.FailSecondScan = true;

        var stopped = await StoppedAt(await world.CommitAsync("newdir"), SelectionStep.Addition);

        await Assert.That(stopped.Failure).IsEqualTo("wc.db went away");
        await Assert.That(world.Calls.Last()).IsEqualTo($"add {At("newdir")}");
    }

    private static readonly string[] TheMixedSelection =
    [
        "art/new.png",
        "art/old.png",
        "art/hero.png",
        "art/protagonist.png",
    ];

    private static World TheMixedWorld() =>
        new(
            [
                Entry("art/new.png", NodeStatus.Unversioned),
                Entry("art/old.png", NodeStatus.Missing),
                Entry("art/hero.png", NodeStatus.Missing),
                Entry("art/protagonist.png", NodeStatus.Unversioned),
            ],
            [HeroRenamed]
        );

    private static async Task<SelectionNotCommittedResponse> StoppedAt(
        DaemonResponse response,
        SelectionStep step
    )
    {
        await Assert.That(response).IsTypeOf<SelectionNotCommittedResponse>();
        var stopped = (SelectionNotCommittedResponse)response;
        await Assert.That(stopped.FailedStep).IsEqualTo(step);
        return stopped;
    }

    private static string At(string relPath) =>
        Path.Combine(Root, relPath.Replace('/', Path.DirectorySeparatorChar));

    private static WorkingCopyEntry Entry(
        string relPath,
        NodeStatus status,
        NodeKind kind = NodeKind.File
    ) =>
        new(
            relPath,
            kind,
            status,
            PropertyStatus.Unmodified,
            Revision: status is NodeStatus.Unversioned ? null : 1,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );

    /// <summary>A working copy and a client that writes down every call made to it, in order.</summary>
    private sealed class World
    {
        private readonly WorkingCopySession _session;

        public World(
            IReadOnlyList<WorkingCopyEntry> entries,
            IReadOnlyList<UnrecordedMove>? moves = null
        )
        {
            Scan = new FakeWorkingCopyScan(Root) { Entries = entries };
            Scan.DuringScan = () =>
            {
                if (FailSecondScan && Scan.Scans == 2)
                {
                    throw new WcDbException(WcDbFailure.Unreadable, "wc.db went away");
                }
            };
            _session = new WorkingCopySession(
                Scan,
                new FakeChangeNotifier(),
                unrecordedMoves: new FakeUnrecordedMoveScan { Moves = moves ?? [] }
            );
        }

        public FakeWorkingCopyScan Scan { get; }

        public List<string> Calls { get; } = [];

        public string? Failing { get; set; }

        public MoveRoute Route
        {
            set => RouteFor = _ => value;
        }

        public Func<string, MoveRoute> RouteFor { get; set; } = _ => MoveRoute.AlreadyRenamed;

        public long? Revision { get; set; } = 7;

        public IReadOnlyList<WorkingCopyEntry>? AfterAdding { get; set; }

        public bool FailSecondScan { get; set; }

        public Task<DaemonResponse> CommitAsync(params string[] ticked) =>
            new SelectionCommitter(RenameAsync, AddAsync, DeleteAsync, SendAsync).CommitAsync(
                _session,
                new CommitSelectionRequest([.. ticked.Select(At)], "a message"),
                None
            );

        private Task<MoveOutcome> RenameAsync(
            string root,
            string source,
            string destination,
            CancellationToken cancellationToken
        )
        {
            Calls.Add($"move {source} -> {destination}");
            FailIf("move");
            var route = RouteFor(source);
            return Task.FromResult(
                new MoveOutcome(
                    route,
                    MoveRefusal.Explain(route, source, destination) is null ? "moved\n" : ""
                )
            );
        }

        private Task<string> AddAsync(
            string root,
            IReadOnlyList<string> paths,
            CancellationToken cancellationToken
        )
        {
            Calls.Add($"add {string.Join(' ', paths)}");
            FailIf("add");
            if (AfterAdding is { } after)
            {
                Scan.Entries = after;
            }

            return Task.FromResult("added\n");
        }

        private Task<string> DeleteAsync(
            string root,
            IReadOnlyList<string> paths,
            CancellationToken cancellationToken
        )
        {
            Calls.Add($"delete {string.Join(' ', paths)}");
            FailIf("delete");
            return Task.FromResult("deleted\n");
        }

        private Task<CommitOutcome> SendAsync(
            string root,
            IReadOnlyList<string> paths,
            string message,
            CommitScope scope,
            CancellationToken cancellationToken
        )
        {
            Calls.Add($"commit {scope} {string.Join(' ', paths)}");
            FailIf("commit");
            return Task.FromResult(new CommitOutcome(Revision, "committed\n"));
        }

        private void FailIf(string step)
        {
            if (Failing == step)
            {
                throw new SvnCommandException($"svn: the {step} failed");
            }
        }
    }
}
