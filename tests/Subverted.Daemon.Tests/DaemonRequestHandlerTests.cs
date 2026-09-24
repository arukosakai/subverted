using Subverted.Core;
using Subverted.Protocol;
using Subverted.Svn;

namespace Subverted.Daemon.Tests;

public sealed class DaemonRequestHandlerTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>
    /// Absolute in this platform's own spelling. The commands that write compare their targets
    /// against the root, so a fake root of <c>/wc</c> against a target of <c>C:\wc\a.txt</c> would
    /// fail the containment check for a reason that has nothing to do with the rule being tested.
    /// </summary>
    private static readonly string Root = Path.GetFullPath("/wc");

    [Test]
    public async Task A_status_request_answers_with_the_working_copy_it_found()
    {
        var scan = new FakeWorkingCopyScan
        {
            Entries = [Modified("art/hero.png"), Clean("readme")],
        };
        var handler = Handler(_ => new WorkingCopySession(scan, new FakeChangeNotifier()));

        var response = await handler.HandleAsync(Status("/wc/art"), None);

        await Assert.That(response).IsTypeOf<StatusResponse>();
        var status = (StatusResponse)response;
        await Assert.That(status.Info).IsEqualTo(scan.Info);
        await Assert.That(status.Entries.Count).IsEqualTo(1);
        await Assert.That(status.Entries[0].RelPath).IsEqualTo("art/hero.png");
    }

    /// <summary>
    /// The path a request names only finds the working copy; the scope is what narrows the listing.
    /// Asserted with the requested path outside the scope, so a handler that scoped by the path
    /// instead would list the wrong thing.
    /// </summary>
    [Test]
    public async Task A_scoped_status_request_lists_only_what_is_in_scope()
    {
        var scan = new FakeWorkingCopyScan(Root)
        {
            Entries = [Modified("art/hero.png"), Modified("readme.txt")],
        };
        var handler = Handler(_ => new WorkingCopySession(scan, new FakeChangeNotifier()));

        var response = (StatusResponse)
            await handler.HandleAsync(
                Status("/wc/art") with
                {
                    Scope = [Path.GetFullPath("/wc/readme.txt")],
                },
                None
            );

        await Assert
            .That(response.Entries.Select(entry => entry.RelPath))
            .IsEquivalentTo(new[] { "readme.txt" });
    }

    [Test]
    public async Task A_scope_reaching_outside_the_working_copy_is_refused()
    {
        var handler = Handler(_ => new WorkingCopySession(
            new FakeWorkingCopyScan(Root),
            new FakeChangeNotifier()
        ));

        var response = await handler.HandleAsync(
            Status("/wc") with
            {
                Scope = [Path.GetFullPath("/wc/art"), Path.GetFullPath("/elsewhere/x")],
            },
            None
        );

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert
            .That(((ErrorResponse)response).Kind)
            .IsEqualTo(DaemonErrorKind.NotAWorkingCopy);
        await Assert.That(((ErrorResponse)response).Message).Contains("elsewhere");
    }

    /// <summary>
    /// Every path is respelled before anything compares it. The fake spells <c>/other</c> as the
    /// root, so a request in that spelling only lands if respelling came first.
    /// </summary>
    [Test]
    public async Task A_path_in_another_spelling_is_respelled_before_it_is_compared()
    {
        var other = Path.GetFullPath("/other");
        IReadOnlyList<string>? added = null;
        var handler = Handler(
            _ => new WorkingCopySession(
                new FakeWorkingCopyScan(Root) { Entries = [Modified("art/hero.png")] },
                new FakeChangeNotifier()
            ),
            scheduleAddition: (_, paths, _) =>
            {
                added = paths;
                return Task.FromResult("A art/new.png");
            },
            respellPath: path => path.Replace(other, Root, StringComparison.Ordinal)
        );

        var status = await handler.HandleAsync(
            new StatusRequest(other, false, false, Scope: [Path.Combine(other, "art")]),
            None
        );
        await handler.HandleAsync(new AddRequest([Path.Combine(other, "art", "new.png")]), None);

        await Assert
            .That(((StatusResponse)status).Entries.Select(entry => entry.RelPath))
            .IsEquivalentTo(new[] { "art/hero.png" });
        await Assert.That(added).IsEquivalentTo(new[] { Path.Combine(Root, "art", "new.png") });
    }

    /// <summary>
    /// The flag the M1 exit criterion is about. A daemon that always reports cold is one whose
    /// watcher is not keeping up, and a front-end can only say so if this is honest.
    /// </summary>
    [Test]
    public async Task The_first_request_reports_cold_and_the_next_one_reports_warm()
    {
        var handler = Handler(_ => new WorkingCopySession(
            new FakeWorkingCopyScan(),
            new FakeChangeNotifier()
        ));

        var first = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);
        var second = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);

        await Assert.That(first.ServedFromWarmIndex).IsFalse();
        await Assert.That(second.ServedFromWarmIndex).IsTrue();
    }

    /// <summary>Both are read from one scan, so both name it — which is what makes it worth holding.</summary>
    [Test]
    public async Task Two_listings_read_from_one_scan_name_the_same_scan()
    {
        var handler = Handler(_ => new WorkingCopySession(
            new FakeWorkingCopyScan(),
            new FakeChangeNotifier()
        ));

        var first = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);
        var second = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);

        await Assert.That(first.ScanId).IsNotNull();
        await Assert.That(second.ScanId).IsEqualTo(first.ScanId);
    }

    /// <summary>
    /// The once-a-second poll of an everything-listing: a hundred thousand entries that have not
    /// moved are not serialised again, and the caller learns its copy is still the current one.
    /// </summary>
    [Test]
    public async Task A_request_holding_the_current_scan_is_told_nothing_changed_instead_of_sent_it_again()
    {
        var handler = Handler(_ => new WorkingCopySession(
            new FakeWorkingCopyScan { Entries = [Modified("art/hero.png")] },
            new FakeChangeNotifier()
        ));
        var held = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);

        var response = await handler.HandleAsync(
            Status("/wc") with
            {
                HeldScan = held.ScanId,
            },
            None
        );

        await Assert.That(response).IsTypeOf<StatusUnchangedResponse>();
        await Assert.That(((StatusUnchangedResponse)response).ScanId).IsEqualTo(held.ScanId!.Value);
    }

    [Test]
    public async Task A_request_holding_a_scan_from_before_a_change_is_sent_the_new_listing()
    {
        var scan = new FakeWorkingCopyScan { Entries = [Modified("art/hero.png")] };
        var notifier = new FakeChangeNotifier();
        var handler = Handler(_ => new WorkingCopySession(scan, notifier));
        var held = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);

        scan.Entries = [Modified("art/hero.png"), Modified("art/villain.png")];
        notifier.RaiseChanged();
        var response = await handler.HandleAsync(
            Status("/wc") with
            {
                HeldScan = held.ScanId,
            },
            None
        );

        await Assert.That(response).IsTypeOf<StatusResponse>();
        var fresh = (StatusResponse)response;
        await Assert.That(fresh.Entries.Count).IsEqualTo(2);
        await Assert.That(fresh.ScanId).IsNotEqualTo(held.ScanId);
    }

    /// <summary>
    /// A scan id this daemon never issued — one from before a restart — is not the current scan,
    /// so it gets the listing rather than a claim that the caller's copy is still right.
    /// </summary>
    [Test]
    public async Task A_request_holding_a_scan_this_daemon_never_issued_is_sent_the_listing()
    {
        var handler = Handler(_ => new WorkingCopySession(
            new FakeWorkingCopyScan(),
            new FakeChangeNotifier()
        ));
        await handler.HandleAsync(Status("/wc"), None);

        var response = await handler.HandleAsync(
            Status("/wc") with
            {
                HeldScan = Guid.NewGuid(),
            },
            None
        );

        await Assert.That(response).IsTypeOf<StatusResponse>();
    }

    [Test]
    [Arguments(WcDbFailure.NotAWorkingCopy, DaemonErrorKind.NotAWorkingCopy)]
    [Arguments(WcDbFailure.Unreadable, DaemonErrorKind.WorkingCopyUnreadable)]
    public async Task An_svn_failure_keeps_its_meaning_on_the_way_to_the_front_end(
        WcDbFailure failure,
        DaemonErrorKind expected
    )
    {
        var handler = Handler(_ => throw new WcDbException(failure, "the reason"));

        var response = await handler.HandleAsync(Status("/elsewhere"), None);

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert.That(((ErrorResponse)response).Kind).IsEqualTo(expected);
        await Assert.That(((ErrorResponse)response).Message).IsEqualTo("the reason");
    }

    [Test]
    public async Task A_shutdown_request_is_acknowledged_and_acted_on()
    {
        var shutdown = new FakeDaemonShutdown();
        var handler = Handler(_ => throw new InvalidOperationException("not needed"), shutdown);

        var response = await handler.HandleAsync(new ShutdownRequest(), None);

        await Assert.That(response).IsTypeOf<AcknowledgedResponse>();
        await Assert.That(shutdown.Requests).IsEqualTo(1);
    }

    [Test]
    public async Task Nothing_shuts_the_daemon_down_unless_it_was_asked_to()
    {
        var shutdown = new FakeDaemonShutdown();
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(), new FakeChangeNotifier()),
            shutdown
        );

        await handler.HandleAsync(Status("/wc"), None);
        await handler.HandleAsync(new DaemonInfoRequest(), None);

        await Assert.That(shutdown.Requests).IsEqualTo(0);
    }

    /// <summary>
    /// The root the log is read in comes from the session, not from the caller's path: <c>svn</c>
    /// has to run somewhere inside the working copy, and the path the user typed may be a file.
    /// </summary>
    [Test]
    public async Task A_log_request_reads_the_history_in_the_working_copy_that_contains_the_path()
    {
        string? readRoot = null;
        string? readPath = null;
        int? readLimit = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan("/wc"), new FakeChangeNotifier()),
            readRevisionLog: (root, path, limit, _, _) =>
            {
                (readRoot, readPath, readLimit) = (root, path, limit);
                return Task.FromResult<IReadOnlyList<RevisionEntry>>([Revision(42)]);
            }
        );

        var response = await handler.HandleAsync(
            new LogRequest(Path.GetFullPath("/wc/art"), Limit: 10),
            None
        );

        await Assert.That(response).IsTypeOf<LogResponse>();
        await Assert.That(((LogResponse)response).Revisions[0].Revision).IsEqualTo(42L);
        await Assert.That(readRoot).IsEqualTo("/wc");
        await Assert.That(readPath).IsEqualTo(Path.GetFullPath("/wc/art"));
        await Assert.That(readLimit).IsEqualTo(10);
    }

    [Test]
    public async Task A_log_request_with_no_limit_asks_for_the_whole_history()
    {
        int? readLimit = -1;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(), new FakeChangeNotifier()),
            readRevisionLog: (_, _, limit, _, _) =>
            {
                readLimit = limit;
                return Task.FromResult<IReadOnlyList<RevisionEntry>>([]);
            }
        );

        await handler.HandleAsync(new LogRequest(Path.GetFullPath("/wc"), Limit: null), None);

        await Assert.That(readLimit).IsNull();
    }

    [Test]
    public async Task A_diff_request_answers_with_svns_own_text()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(), new FakeChangeNotifier()),
            readWorkingCopyDiff: (_, _, _) => Task.FromResult("--- a\n+++ b\n")
        );

        var response = await handler.HandleAsync(new DiffRequest(Path.GetFullPath("/wc")), None);

        await Assert.That(response).IsTypeOf<DiffResponse>();
        await Assert.That(((DiffResponse)response).UnifiedDiff).IsEqualTo("--- a\n+++ b\n");
    }

    [Test]
    public async Task A_log_request_passes_where_it_starts_on_to_svn()
    {
        HistoryStart? readStart = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(), new FakeChangeNotifier()),
            readRevisionLog: (_, _, _, start, _) =>
            {
                readStart = start;
                return Task.FromResult<IReadOnlyList<RevisionEntry>>([]);
            }
        );

        await handler.HandleAsync(
            new LogRequest(Path.GetFullPath("/wc"), 100, new HistoryFromHead()),
            None
        );

        await Assert.That(readStart).IsEqualTo(new HistoryFromHead());
    }

    [Test]
    public async Task A_log_starting_at_revision_one_is_read()
    {
        HistoryStart? readStart = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(), new FakeChangeNotifier()),
            readRevisionLog: (_, _, _, start, _) =>
            {
                readStart = start;
                return Task.FromResult<IReadOnlyList<RevisionEntry>>([Revision(1)]);
            }
        );

        var response = await handler.HandleAsync(
            new LogRequest(Path.GetFullPath("/wc"), 100, new HistoryFromRevision(1)),
            None
        );

        await Assert.That(response).IsTypeOf<LogResponse>();
        await Assert.That(readStart).IsEqualTo(new HistoryFromRevision(1));
    }

    /// <summary>SVN accepts <c>-r 0:1</c> and lists revision 1, which is not the page asked for.</summary>
    [Test]
    public async Task A_log_starting_below_revision_one_is_refused_without_asking_svn()
    {
        var handler = Handler(_ => new WorkingCopySession(
            new FakeWorkingCopyScan(),
            new FakeChangeNotifier()
        ));

        var response = await handler.HandleAsync(
            new LogRequest(Path.GetFullPath("/wc"), 100, new HistoryFromRevision(0)),
            None
        );

        var error = await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert.That(error!.Kind).IsEqualTo(DaemonErrorKind.RequestRefused);
        await Assert.That(error.Message).Contains("revision 0");
    }

    /// <summary>
    /// The repository root comes from the session, not the request: a front-end knows its working
    /// copy, and which server that copy points at is the working copy's business.
    /// </summary>
    [Test]
    public async Task A_revision_diff_is_read_against_the_repository_the_working_copy_points_at()
    {
        (string Root, string RepositoryRoot, string RepositoryPath, long Revision)? read = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan("/wc"), new FakeChangeNotifier()),
            readRevisionDiff: (root, repositoryRoot, repositoryPath, revision, _) =>
            {
                read = (root, repositoryRoot, repositoryPath, revision);
                return Task.FromResult("Index: a.txt\n");
            }
        );

        var response = await handler.HandleAsync(
            new RevisionDiffRequest(Path.GetFullPath("/wc/art"), "/trunk/a.txt", 1),
            None
        );

        var diff = await Assert.That(response).IsTypeOf<DiffResponse>();
        await Assert.That(diff!.UnifiedDiff).IsEqualTo("Index: a.txt\n");
        await Assert.That(read).IsEqualTo(("/wc", "https://svn.example/repo", "/trunk/a.txt", 1L));
    }

    /// <summary>A negative <c>-c</c> is SVN's way of asking for the change reversed.</summary>
    [Test]
    [Arguments(0L)]
    [Arguments(-3L)]
    public async Task A_revision_diff_below_revision_one_is_refused_without_asking_svn(
        long revision
    )
    {
        var handler = Handler(_ => new WorkingCopySession(
            new FakeWorkingCopyScan(),
            new FakeChangeNotifier()
        ));

        var response = await handler.HandleAsync(
            new RevisionDiffRequest(Path.GetFullPath("/wc"), "/a.txt", revision),
            None
        );

        var error = await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert.That(error!.Kind).IsEqualTo(DaemonErrorKind.RequestRefused);
        await Assert.That(error.Message).Contains($"Revision {revision}");
    }

    [Test]
    public async Task A_revision_diff_of_a_path_with_no_leading_slash_is_refused_without_asking_svn()
    {
        var handler = Handler(_ => new WorkingCopySession(
            new FakeWorkingCopyScan(),
            new FakeChangeNotifier()
        ));

        var response = await handler.HandleAsync(
            new RevisionDiffRequest(Path.GetFullPath("/wc"), "a.txt", 2),
            None
        );

        var error = await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert.That(error!.Kind).IsEqualTo(DaemonErrorKind.RequestRefused);
        await Assert.That(error.Message).Contains("'a.txt'");
    }

    [Test]
    public async Task A_working_copy_revision_request_answers_with_the_range_below_the_path()
    {
        (string Root, string Path)? read = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan("/wc"), new FakeChangeNotifier()),
            readBaseRevisionRange: (root, path, _) =>
            {
                read = (root, path);
                return Task.FromResult<BaseRevisionRange?>(new BaseRevisionRange(3, 5));
            }
        );

        var response = await handler.HandleAsync(
            new WorkingCopyRevisionRequest(Path.GetFullPath("/wc/art")),
            None
        );

        await Assert
            .That(response)
            .IsEqualTo(new WorkingCopyRevisionResponse(new BaseRevisionRange(3, 5)));
        await Assert.That(read).IsEqualTo(("/wc", Path.GetFullPath("/wc/art")));
    }

    [Test]
    public async Task A_history_read_that_svn_refuses_is_reported_as_an_svn_failure()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(), new FakeChangeNotifier()),
            readRevisionDiff: (_, _, _, _, _) =>
                throw new SvnCommandException("svn: E160013: path not found"),
            readBaseRevisionRange: (_, _, _) =>
                throw new SvnCommandException("svn: E155021: client too old")
        );

        foreach (
            DaemonRequest request in (DaemonRequest[])
                [
                    new RevisionDiffRequest(Path.GetFullPath("/wc"), "/a.txt", 2),
                    new WorkingCopyRevisionRequest(Path.GetFullPath("/wc")),
                ]
        )
        {
            var response = await handler.HandleAsync(request, None);

            var error = await Assert.That(response).IsTypeOf<ErrorResponse>();
            await Assert.That(error!.Kind).IsEqualTo(DaemonErrorKind.SvnCommandFailed);
        }
    }

    /// <summary>
    /// `svn` failing is not the daemon failing. The front-end needs to be able to say "your
    /// server said no" rather than "Subverted is broken", and the exit codes differ.
    /// </summary>
    [Test]
    public async Task An_svn_command_that_fails_is_reported_as_such_and_not_as_an_internal_error()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(), new FakeChangeNotifier()),
            readRevisionLog: (_, _, _, _, _) =>
                throw new SvnCommandException("svn: E170013: unable to connect"),
            readWorkingCopyDiff: (_, _, _) =>
                throw new SvnCommandException("svn: E170013: unable to connect")
        );

        foreach (
            DaemonRequest request in (DaemonRequest[])
                [
                    new LogRequest(Path.GetFullPath("/wc"), null),
                    new DiffRequest(Path.GetFullPath("/wc")),
                ]
        )
        {
            var response = await handler.HandleAsync(request, None);

            await Assert.That(response).IsTypeOf<ErrorResponse>();
            await Assert
                .That(((ErrorResponse)response).Kind)
                .IsEqualTo(DaemonErrorKind.SvnCommandFailed);
        }
    }

    /// <summary>Shelling out reports a missing working copy the same way status does.</summary>
    [Test]
    public async Task A_log_or_diff_outside_any_working_copy_is_the_same_error_status_gives()
    {
        var handler = Handler(_ =>
            throw new WcDbException(WcDbFailure.NotAWorkingCopy, "nothing here")
        );

        var response = await handler.HandleAsync(
            new DiffRequest(Path.GetFullPath("/nowhere")),
            None
        );

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert
            .That(((ErrorResponse)response).Kind)
            .IsEqualTo(DaemonErrorKind.NotAWorkingCopy);
    }

    [Test]
    public async Task An_add_request_schedules_the_paths_it_named_in_the_root_that_contains_them()
    {
        string? addedIn = null;
        IReadOnlyList<string>? added = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            scheduleAddition: (root, paths, _) =>
            {
                (addedIn, added) = (root, paths);
                return Task.FromResult("A         art/hero.png\n");
            }
        );

        var response = await handler.HandleAsync(
            new AddRequest([Path.Combine(Root, "art", "hero.png")]),
            None
        );

        await Assert.That(response).IsTypeOf<AddResponse>();
        await Assert
            .That(((AddResponse)response).Notifications)
            .IsEqualTo("A         art/hero.png\n");
        await Assert.That(addedIn).IsEqualTo(Root);
        await Assert.That(added).IsEquivalentTo([Path.Combine(Root, "art", "hero.png")]);
    }

    [Test]
    public async Task A_revert_request_reverts_the_paths_it_named()
    {
        IReadOnlyList<string>? reverted = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            revertChanges: (_, paths, _) =>
            {
                reverted = paths;
                return Task.FromResult("Reverted 'art/hero.png'\n");
            }
        );

        var response = await handler.HandleAsync(
            new RevertRequest([Path.Combine(Root, "art", "hero.png")]),
            None
        );

        await Assert.That(response).IsTypeOf<RevertResponse>();
        await Assert
            .That(((RevertResponse)response).Notifications)
            .IsEqualTo("Reverted 'art/hero.png'\n");
        await Assert.That(reverted).IsEquivalentTo([Path.Combine(Root, "art", "hero.png")]);
    }

    [Test]
    public async Task A_delete_request_reaches_the_client_and_answers_with_its_notifications()
    {
        IReadOnlyList<string>? deleted = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            scheduleDeletion: (_, paths, _) =>
            {
                deleted = paths;
                return Task.FromResult("D         art/hero.png\n");
            }
        );

        var response = await handler.HandleAsync(
            new DeleteRequest([Path.Combine(Root, "art", "hero.png")]),
            None
        );

        await Assert.That(response).IsTypeOf<DeleteResponse>();
        await Assert
            .That(((DeleteResponse)response).Notifications)
            .IsEqualTo("D         art/hero.png\n");
        await Assert.That(deleted).IsEquivalentTo([Path.Combine(Root, "art", "hero.png")]);
    }

    [Test]
    [Arguments(MoveRoute.Ordinary)]
    [Arguments(MoveRoute.AlreadyRenamed)]
    public async Task A_move_that_happened_answers_with_its_route(MoveRoute route)
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            renameNode: (_, _, _, _) =>
                Task.FromResult(new MoveOutcome(route, "A         art/protagonist.png\n"))
        );

        var response = await handler.HandleAsync(Rename(), None);

        await Assert.That(response).IsTypeOf<MoveResponse>();
        await Assert.That(((MoveResponse)response).Route).IsEqualTo(route);
        await Assert
            .That(((MoveResponse)response).Notifications)
            .IsEqualTo("A         art/protagonist.png\n");
    }

    /// <summary>
    /// A refused rename is not a <see cref="MoveResponse"/> with empty notifications — it is an
    /// error, because the front-end printing "nothing renamed" would read as "there was nothing to
    /// do" rather than "this could not be done".
    /// </summary>
    [Test]
    [Arguments(MoveRoute.NothingAtSource)]
    [Arguments(MoveRoute.DestinationOccupied)]
    [Arguments(MoveRoute.AlreadyRenamedDirectory)]
    public async Task A_refused_move_comes_back_as_an_error(MoveRoute route)
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            renameNode: (_, _, _, _) => Task.FromResult(new MoveOutcome(route, string.Empty))
        );

        var response = await handler.HandleAsync(Rename(), None);

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert.That(((ErrorResponse)response).Kind).IsEqualTo(DaemonErrorKind.RequestRefused);
        await Assert.That(((ErrorResponse)response).Message).IsNotEmpty();
    }

    /// <summary>
    /// Both ends are checked against the root, not only the source: a destination in another working
    /// copy would have SVN record a copy between repositories, which is not the operation anyone
    /// typed.
    /// </summary>
    [Test]
    public async Task A_move_whose_destination_leaves_the_working_copy_is_refused()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            renameNode: (_, _, _, _) =>
                throw new InvalidOperationException("This move should not have been run.")
        );

        var response = await handler.HandleAsync(
            new MoveRequest(
                Path.Combine(Root, "art", "hero.png"),
                Path.Combine(Path.GetTempPath(), "elsewhere", "hero.png")
            ),
            None
        );

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert
            .That(((ErrorResponse)response).Kind)
            .IsEqualTo(DaemonErrorKind.NotAWorkingCopy);
    }

    private static MoveRequest Rename() =>
        new(Path.Combine(Root, "art", "hero.png"), Path.Combine(Root, "art", "protagonist.png"));

    [Test]
    public async Task A_commit_request_carries_the_message_through_and_answers_with_the_revision()
    {
        string? committedMessage = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            commitChanges: (_, _, message, _, _) =>
            {
                committedMessage = message;
                return Task.FromResult(new CommitOutcome(42, "Sending art/hero.png\n"));
            }
        );

        var response = await handler.HandleAsync(
            new CommitRequest([Path.Combine(Root, "art")], "re-export hero"),
            None
        );

        await Assert.That(response).IsTypeOf<CommitResponse>();
        await Assert.That(((CommitResponse)response).Revision).IsEqualTo(42L);
        await Assert
            .That(((CommitResponse)response).Notifications)
            .IsEqualTo("Sending art/hero.png\n");
        await Assert.That(committedMessage).IsEqualTo("re-export hero");
    }

    /// <summary>
    /// The scope is what separates a picked set from a subtree. Dropping it here would let a
    /// picked directory take the children its owner had just declined, and the commit would still
    /// report success.
    /// </summary>
    [Test]
    [Arguments(CommitScope.WholeSubtree)]
    [Arguments(CommitScope.ExactlyTheseNodes)]
    public async Task A_commit_request_carries_its_scope_through_to_the_client(CommitScope scope)
    {
        CommitScope? committedScope = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            commitChanges: (_, _, _, requested, _) =>
            {
                committedScope = requested;
                return Task.FromResult(new CommitOutcome(42, string.Empty));
            }
        );

        await handler.HandleAsync(new CommitRequest([Root], "picked", scope), None);

        await Assert.That(committedScope).IsEqualTo(scope);
    }

    /// <summary>
    /// SVN says nothing and exits zero when there was nothing to send. That is success, and a
    /// front-end told otherwise would cry wolf on every no-op commit.
    /// </summary>
    [Test]
    public async Task A_commit_with_nothing_to_send_answers_without_a_revision_rather_than_failing()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            commitChanges: (_, _, _, _, _) => Task.FromResult(new CommitOutcome(null, string.Empty))
        );

        var response = await handler.HandleAsync(new CommitRequest([Root], "nothing"), None);

        await Assert.That(response).IsTypeOf<CommitResponse>();
        await Assert.That(((CommitResponse)response).Revision).IsNull();
    }

    /// <summary>
    /// One request, one working copy. Committing a set of paths spanning two of them would send
    /// half of each and report a single revision for it.
    /// </summary>
    [Test]
    public async Task A_path_outside_the_working_copy_the_request_resolved_to_is_refused()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            commitChanges: (_, _, _, _, _) =>
                throw new InvalidOperationException("Nothing should have been committed.")
        );

        var response = await handler.HandleAsync(
            new CommitRequest(
                [Path.Combine(Root, "art"), Path.GetFullPath("/elsewhere/art")],
                "two working copies"
            ),
            None
        );

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert
            .That(((ErrorResponse)response).Kind)
            .IsEqualTo(DaemonErrorKind.NotAWorkingCopy);
        await Assert.That(((ErrorResponse)response).Message).Contains("elsewhere");
    }

    [Test]
    public async Task Every_path_inside_the_same_working_copy_is_accepted()
    {
        IReadOnlyList<string>? committed = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            commitChanges: (_, paths, _, _, _) =>
            {
                committed = paths;
                return Task.FromResult(new CommitOutcome(7, string.Empty));
            }
        );

        var response = await handler.HandleAsync(
            new CommitRequest(
                [Path.Combine(Root, "art"), Path.Combine(Root, "src", "a.txt")],
                "both"
            ),
            None
        );

        await Assert.That(response).IsTypeOf<CommitResponse>();
        await Assert.That(committed?.Count).IsEqualTo(2);
    }

    /// <summary>
    /// The root itself is inside the working copy. A containment test written as "starts with the
    /// root and then a separator" would reject it, and `sv commit` with no path would never work.
    /// </summary>
    [Test]
    public async Task The_working_copy_root_itself_is_an_acceptable_target()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            commitChanges: (_, _, _, _, _) => Task.FromResult(new CommitOutcome(7, string.Empty))
        );

        var response = await handler.HandleAsync(new CommitRequest([Root], "everything"), None);

        await Assert.That(response).IsTypeOf<CommitResponse>();
    }

    [Test]
    public async Task An_update_request_carries_its_path_through_and_answers_with_what_svn_counted()
    {
        string? updated = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            bringUpToDate: (_, path, _) =>
            {
                updated = path;
                return Task.FromResult(new UpdateOutcome(9, 2, 1, "U    src/a.txt\n"));
            }
        );

        var response = await handler.HandleAsync(
            new UpdateRequest(Path.Combine(Root, "src")),
            None
        );

        await Assert.That(response).IsTypeOf<UpdateResponse>();
        await Assert.That(((UpdateResponse)response).Revision).IsEqualTo(9L);
        await Assert.That(((UpdateResponse)response).Conflicts).IsEqualTo(2);
        await Assert.That(((UpdateResponse)response).SkippedPaths).IsEqualTo(1);
        await Assert.That(((UpdateResponse)response).Notifications).IsEqualTo("U    src/a.txt\n");
        await Assert.That(updated).IsEqualTo(Path.Combine(Root, "src"));
    }

    /// <summary>
    /// An update rewrites the files the held index describes, so it has to drop that index for the
    /// same reason the three writing commands do — and more so, because it can change every node in
    /// the tree at once rather than the ones somebody named.
    /// </summary>
    [Test]
    public async Task An_update_drops_the_held_index()
    {
        var scan = new FakeWorkingCopyScan(Root);
        var handler = Handler(
            _ => new WorkingCopySession(scan, new FakeChangeNotifier()),
            bringUpToDate: (_, _, _) =>
                Task.FromResult(new UpdateOutcome(2, 0, 0, "U    src/a.txt\n"))
        );
        await handler.HandleAsync(Status("/wc"), None);
        var warmBefore = (
            (StatusResponse)await handler.HandleAsync(Status("/wc"), None)
        ).ServedFromWarmIndex;

        await handler.HandleAsync(new UpdateRequest(Root), None);
        var after = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);

        await Assert.That(warmBefore).IsTrue();
        await Assert.That(after.ServedFromWarmIndex).IsFalse();
    }

    [Test]
    public async Task A_lock_request_carries_its_paths_comment_and_stealing_through_to_the_client()
    {
        string? lockedIn = null;
        IReadOnlyList<string>? locked = null;
        string? comment = null;
        ForeignLock? foreign = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            acquireLocks: (root, paths, requestedComment, requestedForeign, _) =>
            {
                (lockedIn, locked, comment, foreign) = (
                    root,
                    paths,
                    requestedComment,
                    requestedForeign
                );
                return Task.FromResult(
                    new LockOutcome("'hero.png' locked by user 'ada'.\n", ["svn: warning: W1"])
                );
            }
        );

        var response = await handler.HandleAsync(
            new LockRequest(
                [Path.Combine(Root, "art", "hero.png")],
                "retouching",
                ForeignLock.Overridden
            ),
            None
        );

        await Assert.That(response).IsTypeOf<LockResponse>();
        await Assert
            .That(((LockResponse)response).Notifications)
            .IsEqualTo("'hero.png' locked by user 'ada'.\n");
        await Assert.That(((LockResponse)response).Refusals).IsEquivalentTo(["svn: warning: W1"]);
        await Assert.That(lockedIn).IsEqualTo(Root);
        await Assert.That(locked).IsEquivalentTo([Path.Combine(Root, "art", "hero.png")]);
        await Assert.That(comment).IsEqualTo("retouching");
        await Assert.That(foreign).IsEqualTo(ForeignLock.Overridden);
    }

    [Test]
    public async Task An_unlock_request_carries_its_paths_and_breaking_through_to_the_client()
    {
        IReadOnlyList<string>? unlocked = null;
        ForeignLock? foreign = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            releaseLocks: (_, paths, requestedForeign, _) =>
            {
                (unlocked, foreign) = (paths, requestedForeign);
                return Task.FromResult(new LockOutcome("'hero.png' unlocked.\n", []));
            }
        );

        var response = await handler.HandleAsync(
            new UnlockRequest([Path.Combine(Root, "art", "hero.png")], ForeignLock.Overridden),
            None
        );

        await Assert.That(response).IsTypeOf<UnlockResponse>();
        await Assert
            .That(((UnlockResponse)response).Notifications)
            .IsEqualTo("'hero.png' unlocked.\n");
        await Assert.That(((UnlockResponse)response).Refusals).IsEmpty();
        await Assert.That(unlocked).IsEquivalentTo([Path.Combine(Root, "art", "hero.png")]);
        await Assert.That(foreign).IsEqualTo(ForeignLock.Overridden);
    }

    /// <summary>
    /// A lock token lands in wc.db and <c>sv st</c> reads it out of the held index as <c>K</c>, so
    /// an index that outlived the client would answer "you hold this" about a lock that was refused.
    /// </summary>
    [Test]
    [Arguments("lock")]
    [Arguments("unlock")]
    public async Task Locking_drops_the_held_index(string operation)
    {
        var scan = new FakeWorkingCopyScan(Root);
        var handler = Handler(
            _ => new WorkingCopySession(scan, new FakeChangeNotifier()),
            acquireLocks: (_, _, _, _, _) => Task.FromResult(new LockOutcome(string.Empty, [])),
            releaseLocks: (_, _, _, _) => Task.FromResult(new LockOutcome(string.Empty, []))
        );
        await handler.HandleAsync(Status("/wc"), None);
        var warmBefore = (
            (StatusResponse)await handler.HandleAsync(Status("/wc"), None)
        ).ServedFromWarmIndex;

        await handler.HandleAsync(Locking(operation, Path.Combine(Root, "a.txt")), None);
        var after = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);

        await Assert.That(warmBefore).IsTrue();
        await Assert.That(after.ServedFromWarmIndex).IsFalse();
    }

    [Test]
    [Arguments("lock")]
    [Arguments("unlock")]
    public async Task A_client_that_refuses_a_lock_outright_is_reported_as_an_svn_failure(
        string operation
    )
    {
        var refuse = new SvnCommandException("svn: E155008: The node 'art' is not a file");
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            acquireLocks: (_, _, _, _, _) => throw refuse,
            releaseLocks: (_, _, _, _) => throw refuse
        );

        var response = await handler.HandleAsync(
            Locking(operation, Path.Combine(Root, "art")),
            None
        );

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert
            .That(((ErrorResponse)response).Kind)
            .IsEqualTo(DaemonErrorKind.SvnCommandFailed);
    }

    [Test]
    [Arguments("lock")]
    [Arguments("unlock")]
    public async Task A_lock_naming_a_path_in_another_working_copy_is_refused(string operation)
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            acquireLocks: (_, _, _, _, _) =>
                throw new InvalidOperationException("Nothing should have been locked."),
            releaseLocks: (_, _, _, _) =>
                throw new InvalidOperationException("Nothing should have been unlocked.")
        );

        DaemonRequest request =
            operation == "lock"
                ? new LockRequest(
                    [Path.Combine(Root, "a.txt"), Path.GetFullPath("/elsewhere/a.txt")],
                    null
                )
                : new UnlockRequest([
                    Path.Combine(Root, "a.txt"),
                    Path.GetFullPath("/elsewhere/a.txt"),
                ]);

        var response = await handler.HandleAsync(request, None);

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert
            .That(((ErrorResponse)response).Kind)
            .IsEqualTo(DaemonErrorKind.NotAWorkingCopy);
    }

    [Test]
    public async Task A_resolve_request_carries_its_paths_and_chosen_version_through_to_the_client()
    {
        string? resolvedIn = null;
        IReadOnlyList<string>? resolved = null;
        ConflictResolution? resolution = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            resolveConflicts: (root, paths, requestedResolution, _) =>
            {
                (resolvedIn, resolved, resolution) = (root, paths, requestedResolution);
                return Task.FromResult(
                    new ResolveOutcome(["art/hero.png"], ["svn: warning: W155027"])
                );
            }
        );

        var response = await handler.HandleAsync(
            new ResolveRequest([Path.Combine(Root, "art", "hero.png")], ConflictResolution.Theirs),
            None
        );

        await Assert.That(response).IsTypeOf<ResolveResponse>();
        await Assert
            .That(((ResolveResponse)response).ResolvedPaths)
            .IsEquivalentTo(["art/hero.png"]);
        await Assert
            .That(((ResolveResponse)response).Refusals)
            .IsEquivalentTo(["svn: warning: W155027"]);
        await Assert.That(resolvedIn).IsEqualTo(Root);
        await Assert.That(resolved).IsEquivalentTo([Path.Combine(Root, "art", "hero.png")]);
        await Assert.That(resolution).IsEqualTo(ConflictResolution.Theirs);
    }

    /// <summary>
    /// Every version but the working one rewrites the file, and the conflict flag it clears is the
    /// <c>C</c> the held index read out of wc.db — so an index that survived would be wrong about
    /// both the contents and the state.
    /// </summary>
    [Test]
    public async Task Resolving_drops_the_held_index()
    {
        var scan = new FakeWorkingCopyScan(Root);
        var handler = Handler(
            _ => new WorkingCopySession(scan, new FakeChangeNotifier()),
            resolveConflicts: (_, _, _, _) => Task.FromResult(new ResolveOutcome(["a.txt"], []))
        );
        await handler.HandleAsync(Status("/wc"), None);
        var warmBefore = (
            (StatusResponse)await handler.HandleAsync(Status("/wc"), None)
        ).ServedFromWarmIndex;

        await handler.HandleAsync(
            new ResolveRequest([Path.Combine(Root, "a.txt")], ConflictResolution.Mine),
            None
        );
        var after = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);

        await Assert.That(warmBefore).IsTrue();
        await Assert.That(after.ServedFromWarmIndex).IsFalse();
    }

    [Test]
    public async Task A_client_that_fails_a_resolve_outright_is_reported_as_an_svn_failure()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            resolveConflicts: (_, _, _, _) =>
                throw new SvnCommandException("svn: E155004: Working copy 'C:\\wc' locked.")
        );

        var response = await handler.HandleAsync(
            new ResolveRequest([Path.Combine(Root, "a.txt")], ConflictResolution.Mine),
            None
        );

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert
            .That(((ErrorResponse)response).Kind)
            .IsEqualTo(DaemonErrorKind.SvnCommandFailed);
    }

    [Test]
    public async Task A_resolve_naming_a_path_in_another_working_copy_is_refused()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            resolveConflicts: (_, _, _, _) =>
                throw new InvalidOperationException("Nothing should have been resolved.")
        );

        var response = await handler.HandleAsync(
            new ResolveRequest(
                [Path.Combine(Root, "a.txt"), Path.GetFullPath("/elsewhere/a.txt")],
                ConflictResolution.Mine
            ),
            None
        );

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert
            .That(((ErrorResponse)response).Kind)
            .IsEqualTo(DaemonErrorKind.NotAWorkingCopy);
    }

    /// <summary>
    /// The request names a path and the cleanup runs at that path's <em>root</em>: cleanup's two
    /// halves scope differently, so a run aimed at a subtree can exit zero having left the working
    /// copy locked.
    /// </summary>
    [Test]
    public async Task A_cleanup_request_is_carried_through_to_the_client_as_the_root()
    {
        string? cleanedIn = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            cleanUpWorkingCopy: (root, _) =>
            {
                cleanedIn = root;
                return Task.FromResult(new CleanupOutcome([string.Empty], 2, []));
            }
        );

        var response = await handler.HandleAsync(
            new CleanupRequest(Path.Combine(Root, "art", "hero.png")),
            None
        );

        await Assert.That(response).IsTypeOf<CleanupResponse>();
        await Assert
            .That(((CleanupResponse)response).ReleasedWriteLocks)
            .IsEquivalentTo([string.Empty]);
        await Assert.That(((CleanupResponse)response).FinishedOperations).IsEqualTo(2);
        await Assert.That(((CleanupResponse)response).RemainingWriteLocks).IsEmpty();
        await Assert.That(cleanedIn).IsEqualTo(Root);
    }

    /// <summary>
    /// What cleanup clears is the state that was making every other write fail, and the held index
    /// was read while it stood. Keeping it would answer from a reading taken of a wedged copy.
    /// </summary>
    [Test]
    public async Task Cleaning_up_drops_the_held_index()
    {
        var scan = new FakeWorkingCopyScan(Root);
        var handler = Handler(
            _ => new WorkingCopySession(scan, new FakeChangeNotifier()),
            cleanUpWorkingCopy: (_, _) => Task.FromResult(new CleanupOutcome([string.Empty], 0, []))
        );
        await handler.HandleAsync(Status("/wc"), None);
        var warmBefore = (
            (StatusResponse)await handler.HandleAsync(Status("/wc"), None)
        ).ServedFromWarmIndex;

        await handler.HandleAsync(new CleanupRequest(Path.Combine(Root, "a.txt")), None);
        var after = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);

        await Assert.That(warmBefore).IsTrue();
        await Assert.That(after.ServedFromWarmIndex).IsFalse();
    }

    [Test]
    public async Task A_client_that_fails_a_cleanup_outright_is_reported_as_an_svn_failure()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            cleanUpWorkingCopy: (_, _) =>
                throw new SvnCommandException("svn: E155009: Failed to run the WC DB work queue")
        );

        var response = await handler.HandleAsync(
            new CleanupRequest(Path.Combine(Root, "a.txt")),
            None
        );

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert
            .That(((ErrorResponse)response).Kind)
            .IsEqualTo(DaemonErrorKind.SvnCommandFailed);
    }

    [Test]
    [Arguments("add")]
    [Arguments("revert")]
    [Arguments("commit")]
    [Arguments("lock")]
    [Arguments("unlock")]
    [Arguments("resolve")]
    [Arguments("delete")]
    public async Task A_request_that_writes_and_names_no_paths_is_refused_rather_than_guessed_at(
        string operation
    )
    {
        var handler = Handler(_ =>
            throw new InvalidOperationException("No session should have been opened.")
        );

        DaemonRequest request = operation switch
        {
            "add" => new AddRequest([]),
            "revert" => new RevertRequest([]),
            "lock" => new LockRequest([], null),
            "unlock" => new UnlockRequest([]),
            "resolve" => new ResolveRequest([], ConflictResolution.Mine),
            "delete" => new DeleteRequest([]),
            _ => new CommitRequest([], "nothing named"),
        };

        var response = await handler.HandleAsync(request, None);

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert.That(((ErrorResponse)response).Kind).IsEqualTo(DaemonErrorKind.Internal);
        await Assert.That(((ErrorResponse)response).Message).Contains(operation);
    }

    /// <summary>
    /// The race the watcher cannot close on its own: `sv add x && sv st` runs the two in sequence,
    /// and a filesystem event that has not arrived yet would leave the second answering from an
    /// index taken before the first.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Writing_drops_the_held_index_whether_or_not_the_client_succeeded(
        bool clientSucceeds
    )
    {
        var scan = new FakeWorkingCopyScan(Root);
        var handler = Handler(
            _ => new WorkingCopySession(scan, new FakeChangeNotifier()),
            scheduleAddition: (_, _, _) =>
                clientSucceeds
                    ? Task.FromResult("A         a.txt\n")
                    : throw new SvnCommandException("svn: E200009: refused one of them")
        );
        await handler.HandleAsync(Status("/wc"), None);
        var warmBefore = (
            (StatusResponse)await handler.HandleAsync(Status("/wc"), None)
        ).ServedFromWarmIndex;

        await handler.HandleAsync(new AddRequest([Path.Combine(Root, "a.txt")]), None);
        var after = (StatusResponse)await handler.HandleAsync(Status("/wc"), None);

        await Assert.That(warmBefore).IsTrue();
        await Assert.That(after.ServedFromWarmIndex).IsFalse();
    }

    [Test]
    [Arguments("add")]
    [Arguments("revert")]
    [Arguments("commit")]
    [Arguments("update")]
    public async Task A_client_that_refuses_a_write_is_reported_as_an_svn_failure(string operation)
    {
        var refuse = new SvnCommandException(
            "svn: E155007: none of the targets are working copies"
        );
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(Root), new FakeChangeNotifier()),
            scheduleAddition: (_, _, _) => throw refuse,
            revertChanges: (_, _, _) => throw refuse,
            commitChanges: (_, _, _, _, _) => throw refuse,
            bringUpToDate: (_, _, _) => throw refuse
        );

        DaemonRequest request = operation switch
        {
            "add" => new AddRequest([Path.Combine(Root, "a.txt")]),
            "revert" => new RevertRequest([Path.Combine(Root, "a.txt")]),
            "update" => new UpdateRequest(Path.Combine(Root, "a.txt")),
            _ => new CommitRequest([Path.Combine(Root, "a.txt")], "doomed"),
        };

        var response = await handler.HandleAsync(request, None);

        await Assert.That(response).IsTypeOf<ErrorResponse>();
        await Assert
            .That(((ErrorResponse)response).Kind)
            .IsEqualTo(DaemonErrorKind.SvnCommandFailed);
    }

    [Test]
    public async Task Daemon_info_reports_uptime_from_when_the_daemon_started()
    {
        var clock = new StoppedClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan(), new FakeChangeNotifier()),
            clock: clock
        );

        clock.Now = clock.Now.AddSeconds(61.5);
        var response = (DaemonInfoResponse)await handler.HandleAsync(new DaemonInfoRequest(), None);

        await Assert.That(response.UptimeSeconds).IsEqualTo(61.5);
    }

    [Test]
    public async Task Daemon_info_lists_every_working_copy_it_is_holding()
    {
        var scan = new FakeWorkingCopyScan("/wc") { Entries = [Clean("a"), Clean("b")] };
        var notifier = new FakeChangeNotifier();
        var handler = Handler(_ => new WorkingCopySession(scan, notifier));
        await handler.HandleAsync(Status("/wc"), None);
        notifier.IsWatching = false;

        var response = (DaemonInfoResponse)await handler.HandleAsync(new DaemonInfoRequest(), None);

        await Assert.That(response.WorkingCopies.Count).IsEqualTo(1);
        await Assert.That(response.WorkingCopies[0].RootPath).IsEqualTo("/wc");
        await Assert.That(response.WorkingCopies[0].EntryCount).IsEqualTo(2);
        await Assert
            .That(response.WorkingCopies[0].WatcherState)
            .IsEqualTo(WatcherState.Unavailable);
    }

    [Test]
    public async Task Daemon_info_before_anything_was_asked_for_lists_nothing()
    {
        var handler = Handler(_ => throw new InvalidOperationException("not needed"));

        var response = (DaemonInfoResponse)await handler.HandleAsync(new DaemonInfoRequest(), None);

        await Assert.That(response.WorkingCopies).IsEmpty();
        await Assert.That(response.Version).IsNotEmpty();
    }

    [Test]
    public async Task A_diff_asked_for_without_a_context_is_svns_and_says_it_has_three_lines()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan("/wc"), new FakeChangeNotifier()),
            readWorkingCopyDiff: (_, _, _) => Task.FromResult("svn's")
        );

        var response = await handler.HandleAsync(new DiffRequest(Path.GetFullPath("/wc/a")), None);

        await Assert.That(response).IsEqualTo(new DiffResponse("svn's", DiffContext.Default));
    }

    [Test]
    [Arguments(3)]
    [Arguments(2)]
    [Arguments(0)]
    public async Task A_diff_asked_for_with_three_lines_or_fewer_is_svns_without_writing_one(int lines)
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan("/wc"), new FakeChangeNotifier()),
            readWorkingCopyDiff: (_, _, _) => Task.FromResult("svn's")
        );

        var response = await handler.HandleAsync(
            new DiffRequest(Path.GetFullPath("/wc/a"), new DiffContext(lines)),
            None
        );

        await Assert.That(response).IsEqualTo(new DiffResponse("svn's", DiffContext.Default));
    }

    [Test]
    [Arguments(4)]
    [Arguments(null)]
    public async Task A_diff_asked_for_with_more_context_is_written_with_it(int? lines)
    {
        var asked = new DiffContext(lines);
        (string Root, string Path, DiffContext Context)? written = null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan("/wc"), new FakeChangeNotifier()),
            readWorkingCopyContextDiff: (root, path, context, _) =>
            {
                written = (root, path, context);
                return Task.FromResult<string?>("ours");
            }
        );

        var response = await handler.HandleAsync(
            new DiffRequest(Path.GetFullPath("/wc/a"), asked),
            None
        );

        await Assert.That(response).IsEqualTo(new DiffResponse("ours", asked));
        await Assert.That(written).IsEqualTo(("/wc", Path.GetFullPath("/wc/a"), asked));
    }

    [Test]
    public async Task A_file_that_cannot_have_more_context_gets_svns_diff_marked_as_three_lines()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan("/wc"), new FakeChangeNotifier()),
            readWorkingCopyDiff: (_, _, _) => Task.FromResult("svn's"),
            readWorkingCopyContextDiff: (_, _, _, _) => Task.FromResult<string?>(null)
        );

        var response = await handler.HandleAsync(
            new DiffRequest(Path.GetFullPath("/wc/a"), new DiffContext(10)),
            None
        );

        await Assert.That(response).IsEqualTo(new DiffResponse("svn's", DiffContext.Default));
    }

    [Test]
    public async Task A_negative_context_is_refused_without_asking_anything()
    {
        var handler = Handler(_ => new WorkingCopySession(
            new FakeWorkingCopyScan("/wc"),
            new FakeChangeNotifier()
        ));

        var diff = await handler.HandleAsync(
            new DiffRequest(Path.GetFullPath("/wc/a"), new DiffContext(-1)),
            None
        );
        var revisionDiff = await handler.HandleAsync(
            new RevisionDiffRequest(Path.GetFullPath("/wc"), "/a", 2, new DiffContext(-1)),
            None
        );

        foreach (var response in new[] { diff, revisionDiff })
        {
            var error = await Assert.That(response).IsTypeOf<ErrorResponse>();
            await Assert.That(error!.Kind).IsEqualTo(DaemonErrorKind.RequestRefused);
            await Assert.That(error.Message).Contains("-1 lines");
        }
    }

    [Test]
    public async Task A_revision_diff_asked_for_without_a_context_is_svns_marked_as_three_lines()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan("/wc"), new FakeChangeNotifier()),
            readRevisionDiff: (_, _, _, _, _) => Task.FromResult("svn's")
        );

        var response = await handler.HandleAsync(
            new RevisionDiffRequest(Path.GetFullPath("/wc"), "/a", 2, new DiffContext(3)),
            None
        );

        await Assert.That(response).IsEqualTo(new DiffResponse("svn's", DiffContext.Default));
    }

    [Test]
    public async Task A_revision_diff_asked_for_with_more_context_is_written_with_it()
    {
        (string Root, string RepositoryRoot, string Path, long Revision, DiffContext Context)? written =
            null;
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan("/wc"), new FakeChangeNotifier()),
            readRevisionContextDiff: (root, repositoryRoot, path, revision, context, _) =>
            {
                written = (root, repositoryRoot, path, revision, context);
                return Task.FromResult<string?>("ours");
            }
        );

        var response = await handler.HandleAsync(
            new RevisionDiffRequest(Path.GetFullPath("/wc"), "/a", 2, DiffContext.WholeFile),
            None
        );

        await Assert.That(response).IsEqualTo(new DiffResponse("ours", DiffContext.WholeFile));
        await Assert
            .That(written)
            .IsEqualTo(("/wc", "https://svn.example/repo", "/a", 2L, DiffContext.WholeFile));
    }

    [Test]
    public async Task A_revision_that_cannot_have_more_context_gets_svns_diff_marked_as_three_lines()
    {
        var handler = Handler(
            _ => new WorkingCopySession(new FakeWorkingCopyScan("/wc"), new FakeChangeNotifier()),
            readRevisionDiff: (_, _, _, _, _) => Task.FromResult("svn's"),
            readRevisionContextDiff: (_, _, _, _, _, _) => Task.FromResult<string?>(null)
        );

        var response = await handler.HandleAsync(
            new RevisionDiffRequest(Path.GetFullPath("/wc"), "/a", 2, new DiffContext(25)),
            None
        );

        await Assert.That(response).IsEqualTo(new DiffResponse("svn's", DiffContext.Default));
    }

    private static DaemonRequestHandler Handler(
        Func<string, WorkingCopySession> open,
        IDaemonShutdown? shutdown = null,
        TimeProvider? clock = null,
        ReadRevisionLog? readRevisionLog = null,
        ReadWorkingCopyDiff? readWorkingCopyDiff = null,
        ReadRevisionDiff? readRevisionDiff = null,
        ReadWorkingCopyContextDiff? readWorkingCopyContextDiff = null,
        ReadRevisionContextDiff? readRevisionContextDiff = null,
        ReadBaseRevisionRange? readBaseRevisionRange = null,
        ScheduleAddition? scheduleAddition = null,
        RevertChanges? revertChanges = null,
        ScheduleDeletion? scheduleDeletion = null,
        RenameNode? renameNode = null,
        CommitChanges? commitChanges = null,
        BringUpToDate? bringUpToDate = null,
        AcquireLocks? acquireLocks = null,
        ReleaseLocks? releaseLocks = null,
        ResolveConflicts? resolveConflicts = null,
        CleanUpWorkingCopy? cleanUpWorkingCopy = null,
        RespellPath? respellPath = null,
        RecordDeletion? recordDeletion = null
    ) =>
        new(
            // Opening is asynchronous because the CLI fallback is a child process; nothing the
            // handler answers depends on that, so these tests state the open and not the waiting.
            new WorkingCopySessions((path, _) => Task.FromResult(open(path))),
            shutdown ?? new FakeDaemonShutdown(),
            clock ?? TimeProvider.System,
            readRevisionLog ?? NoLog,
            readWorkingCopyDiff ?? NoDiff,
            readRevisionDiff ?? NoRevisionDiff,
            readWorkingCopyContextDiff ?? NoContextDiff,
            readRevisionContextDiff ?? NoRevisionContextDiff,
            readBaseRevisionRange ?? NoBaseRevisionRange,
            scheduleAddition ?? NoAdd,
            revertChanges ?? NoRevert,
            scheduleDeletion ?? NoDelete,
            renameNode ?? NoRename,
            commitChanges ?? NoCommit,
            bringUpToDate ?? NoUpdate,
            acquireLocks ?? NoLock,
            releaseLocks ?? NoUnlock,
            resolveConflicts ?? NoResolve,
            cleanUpWorkingCopy ?? NoCleanup,
            respellPath ?? (path => path),
            new SelectionCommitter(
                renameNode ?? NoRename,
                scheduleAddition ?? NoAdd,
                recordDeletion ?? NoRecordDeletion,
                commitChanges ?? NoCommit
            )
        );

    private static Task<IReadOnlyList<RevisionEntry>> NoLog(
        string root,
        string path,
        int? limit,
        HistoryStart? start,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have read the log.");

    private static Task<string> NoDiff(
        string root,
        string path,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have read a diff.");

    private static Task<string> NoRevisionDiff(
        string root,
        string repositoryRoot,
        string repositoryPath,
        long revision,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have read a revision's diff.");

    private static Task<string?> NoContextDiff(
        string root,
        string path,
        DiffContext context,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have written a diff itself.");

    private static Task<string?> NoRevisionContextDiff(
        string root,
        string repositoryRoot,
        string repositoryPath,
        long revision,
        DiffContext context,
        CancellationToken cancellationToken
    ) =>
        throw new InvalidOperationException(
            "This test should not have written a revision's diff itself."
        );

    private static Task<BaseRevisionRange?> NoBaseRevisionRange(
        string root,
        string path,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have read BASE revisions.");

    /// <summary>
    /// The seven defaults that change a working copy. A test reaching one it did not ask for would
    /// be modifying a working copy, so they throw rather than quietly succeeding.
    /// </summary>
    private static Task<string> NoAdd(
        string root,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have scheduled an addition.");

    private static Task<string> NoRevert(
        string root,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have reverted anything.");

    private static Task<string> NoDelete(
        string root,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have deleted anything.");

    private static Task<string> NoRecordDeletion(
        string root,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have recorded a deletion.");

    private static Task<MoveOutcome> NoRename(
        string root,
        string source,
        string destination,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have renamed anything.");

    private static Task<CommitOutcome> NoCommit(
        string root,
        IReadOnlyList<string> paths,
        string message,
        CommitScope scope,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have committed anything.");

    private static Task<UpdateOutcome> NoUpdate(
        string root,
        string path,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have updated anything.");

    private static Task<LockOutcome> NoLock(
        string root,
        IReadOnlyList<string> paths,
        string? comment,
        ForeignLock foreign,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have locked anything.");

    private static Task<LockOutcome> NoUnlock(
        string root,
        IReadOnlyList<string> paths,
        ForeignLock foreign,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have unlocked anything.");

    private static Task<ResolveOutcome> NoResolve(
        string root,
        IReadOnlyList<string> paths,
        ConflictResolution resolution,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have resolved anything.");

    private static Task<CleanupOutcome> NoCleanup(
        string root,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("This test should not have cleaned anything.");

    private static DaemonRequest Locking(string operation, string path) =>
        operation == "lock" ? new LockRequest([path], null) : new UnlockRequest([path]);

    private static StatusRequest Status(string path) =>
        new(Path.GetFullPath(path), IncludeUnmodified: false, IncludeIgnored: false);

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

    private static WorkingCopyEntry Modified(string relPath) =>
        Clean(relPath) with
        {
            Status = NodeStatus.Modified,
        };

    private static RevisionEntry Revision(long revision) =>
        new(revision, "artist", DateTimeOffset.UnixEpoch, "re-export", []);

    /// <summary>A clock that only moves when a test moves it.</summary>
    private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
