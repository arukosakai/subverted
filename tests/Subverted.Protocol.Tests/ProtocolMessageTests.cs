using System.Text;
using Subverted.Core;

namespace Subverted.Protocol.Tests;

/// <summary>
/// Pins the JSON a daemon and a front-end from two different builds have to agree on. Round-trips
/// prove we can read ourselves; the literal assertions prove we still read the previous build.
/// </summary>
public sealed class ProtocolMessageTests
{
    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task A_status_request_round_trips_both_of_its_switches(
        bool includeUnmodified,
        bool includeIgnored
    )
    {
        var request = new StatusRequest("/wc/art", includeUnmodified, includeIgnored);

        var decoded = ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(request));

        await Assert.That(decoded).IsEqualTo(request);
    }

    /// <summary>
    /// Compared element by element: a record holding a list compares the list by reference, so
    /// equality of the whole request would fail on a correct round trip.
    /// </summary>
    [Test]
    public async Task A_status_request_round_trips_its_scope()
    {
        var request = new StatusRequest("/wc", false, false, Scope: ["/wc/art", "/wc/readme.txt"]);

        var decoded = (StatusRequest)ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(request));

        await Assert.That(decoded.Scope).IsEquivalentTo(new[] { "/wc/art", "/wc/readme.txt" });
    }

    /// <summary>
    /// The whole working copy is the absent scope, not an empty one, and it has to stay absent across
    /// the wire — an empty list would be a listing scoped to nothing.
    /// </summary>
    [Test]
    public async Task A_status_request_without_a_scope_arrives_without_one()
    {
        var decoded = (StatusRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(new StatusRequest("/wc", false, false))
            );

        await Assert.That(decoded.Scope).IsNull();
    }

    [Test]
    public async Task A_request_with_no_arguments_still_arrives_as_itself()
    {
        await Assert
            .That(ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(new DaemonInfoRequest())))
            .IsTypeOf<DaemonInfoRequest>();
        await Assert
            .That(ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(new ShutdownRequest())))
            .IsTypeOf<ShutdownRequest>();
    }

    /// <summary>
    /// The discriminator is the contract between a daemon left running since yesterday and a
    /// front-end upgraded this morning. Renaming a C# type must not quietly rename it.
    /// </summary>
    [Test]
    public async Task Request_kinds_are_named_on_the_wire()
    {
        await Assert
            .That(Json(new StatusRequest("/wc", false, false)))
            .Contains("\"$kind\":\"status\"");
        await Assert.That(Json(new DaemonInfoRequest())).Contains("\"$kind\":\"daemon-info\"");
        await Assert.That(Json(new ShutdownRequest())).Contains("\"$kind\":\"shutdown\"");
    }

    [Test]
    public async Task Response_kinds_are_named_on_the_wire()
    {
        await Assert.That(Json(SampleStatusResponse())).Contains("\"$kind\":\"status\"");
        await Assert
            .That(Json(new DaemonInfoResponse("1.0", 0, [])))
            .Contains("\"$kind\":\"daemon-info\"");
        await Assert.That(Json(new AcknowledgedResponse())).Contains("\"$kind\":\"ack\"");
        await Assert
            .That(Json(new ErrorResponse(DaemonErrorKind.Internal, "x")))
            .Contains("\"$kind\":\"error\"");
    }

    /// <summary>
    /// Written as ordinals, inserting a member into the middle of <see cref="NodeStatus"/> would
    /// silently reinterpret every value after it — a Modified node arriving as Deleted.
    /// </summary>
    [Test]
    public async Task Enum_values_travel_as_names_not_as_ordinals()
    {
        var json = Json(SampleStatusResponse());

        await Assert.That(json).Contains("\"Modified\"");
        await Assert.That(json).Contains("\"File\"");
        await Assert
            .That(Json(new ErrorResponse(DaemonErrorKind.NotAWorkingCopy, "x")))
            .Contains("\"NotAWorkingCopy\"");
    }

    [Test]
    public async Task A_status_response_round_trips_its_entries_and_its_timing()
    {
        var response = SampleStatusResponse();

        var decoded = ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(response));

        await Assert.That(decoded).IsTypeOf<StatusResponse>();
        var status = (StatusResponse)decoded;
        await Assert.That(status.Info).IsEqualTo(response.Info);
        await Assert.That(status.ServedFromWarmIndex).IsTrue();
        await Assert.That(status.ServerElapsedMilliseconds).IsEqualTo(1.5);
        await Assert.That(status.UnfinishedOperations).IsEqualTo(3);
        await Assert.That(status.Entries).IsEquivalentTo(response.Entries);
    }

    /// <summary>
    /// An unversioned node has no revision and no changelist. Either of them coming back as a
    /// default rather than as absent is a status line that claims something that is not true.
    /// </summary>
    [Test]
    public async Task Absent_revision_and_changelist_stay_absent()
    {
        var entry = new WorkingCopyEntry(
            "build/out.obj",
            NodeKind.File,
            NodeStatus.Unversioned,
            PropertyStatus.Unmodified,
            Revision: null,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );
        var response = new StatusResponse(SampleInfo, [entry], false, 0, 0, []);

        var decoded = (StatusResponse)
            ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(response));

        await Assert.That(decoded.Entries[0].Revision).IsNull();
        await Assert.That(decoded.Entries[0].Changelist).IsNull();
        await Assert.That(decoded.Entries[0].OnDisk).IsNull();
    }

    /// <summary>
    /// Two saves a tick apart are the edit the fingerprint exists to see. A wire format that rounded
    /// to the millisecond would hand the front-end two equal entries for them.
    /// </summary>
    [Test]
    public async Task A_fingerprint_round_trips_to_the_tick_and_stays_utc()
    {
        var written = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(1);
        var response = SampleStatusResponse() with
        {
            Entries = [SampleEntry with { OnDisk = new FileFingerprint(4096, written) }],
        };

        var decoded = (StatusResponse)
            ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(response));

        var onDisk = decoded.Entries[0].OnDisk!;
        await Assert.That(onDisk.Length).IsEqualTo(4096);
        await Assert.That(onDisk.LastWriteTimeUtc.Ticks).IsEqualTo(written.Ticks);
        await Assert.That(onDisk.LastWriteTimeUtc.Kind).IsEqualTo(DateTimeKind.Utc);
    }

    /// <summary>
    /// A daemon built before the fingerprint existed sends entries without it. That reads as "not
    /// known", which is true of it, rather than as a malformed message that fails the whole listing.
    /// </summary>
    [Test]
    public async Task An_entry_from_a_daemon_that_predates_the_fingerprint_reads_as_unknown()
    {
        var json = Json(SampleStatusResponse());
        var withoutField = json.Replace(",\"onDisk\":null", string.Empty, StringComparison.Ordinal);

        var decoded = (StatusResponse)
            ProtocolMessage.DecodeResponse(Encoding.UTF8.GetBytes(withoutField));

        await Assert.That(withoutField).DoesNotContain("onDisk");
        await Assert.That(decoded.Entries[0].OnDisk).IsNull();
    }

    [Test]
    public async Task A_status_request_round_trips_the_scan_it_holds()
    {
        var held = Guid.NewGuid();

        var decoded = (StatusRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(new StatusRequest("/wc", true, false, HeldScan: held))
            );

        await Assert.That(decoded.HeldScan).IsEqualTo(held);
    }

    /// <summary>
    /// A front-end built before the field asks without it, and must be sent the listing: a held
    /// scan read as some default would be one it could be told "unchanged" about.
    /// </summary>
    [Test]
    public async Task A_status_request_from_a_front_end_that_predates_held_scans_holds_none()
    {
        var json = Json(new StatusRequest("/wc", false, false));
        var withoutField = json.Replace(
            ",\"heldScan\":null",
            string.Empty,
            StringComparison.Ordinal
        );

        var decoded = (StatusRequest)
            ProtocolMessage.DecodeRequest(Encoding.UTF8.GetBytes(withoutField));

        await Assert.That(withoutField).DoesNotContain("heldScan");
        await Assert.That(decoded.HeldScan).IsNull();
    }

    /// <summary>
    /// The other direction: a daemon older than a front-end skips a field it has never heard of
    /// rather than refusing the request, so a newer front-end asking with a held scan still gets a
    /// listing. Shown with a made-up field, since this build knows every real one.
    /// </summary>
    [Test]
    public async Task A_request_carrying_a_field_this_build_does_not_know_still_arrives()
    {
        var json = Json(new StatusRequest("/wc", false, false));
        var withNewerField = json.Replace(
            "\"workingCopyPath\"",
            "\"fromTheFuture\":\"x\",\"workingCopyPath\"",
            StringComparison.Ordinal
        );

        var decoded = (StatusRequest)
            ProtocolMessage.DecodeRequest(Encoding.UTF8.GetBytes(withNewerField));

        await Assert.That(withNewerField).Contains("fromTheFuture");
        await Assert.That(decoded.WorkingCopyPath).IsEqualTo("/wc");
    }

    [Test]
    public async Task A_status_response_round_trips_the_scan_it_was_read_from()
    {
        var scan = Guid.NewGuid();

        var decoded = (StatusResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(SampleStatusResponse() with { ScanId = scan })
            );

        await Assert.That(decoded.ScanId).IsEqualTo(scan);
    }

    /// <summary>
    /// A daemon built before scan ids answers without one. That reads as "cannot be asked", so the
    /// front-end never sends a held scan it would have to invent.
    /// </summary>
    [Test]
    public async Task A_status_response_from_a_daemon_that_predates_scan_ids_names_no_scan()
    {
        var json = Json(SampleStatusResponse());
        var withoutField = json.Replace(",\"scanId\":null", string.Empty, StringComparison.Ordinal);

        var decoded = (StatusResponse)
            ProtocolMessage.DecodeResponse(Encoding.UTF8.GetBytes(withoutField));

        await Assert.That(withoutField).DoesNotContain("scanId");
        await Assert.That(decoded.ScanId).IsNull();
    }

    [Test]
    public async Task An_unchanged_status_answer_is_named_on_the_wire_and_round_trips()
    {
        var response = new StatusUnchangedResponse(Guid.NewGuid(), 0.25);

        var decoded = ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(response));

        await Assert.That(Json(response)).Contains("\"$kind\":\"status-unchanged\"");
        await Assert.That(decoded).IsEqualTo(response);
    }

    [Test]
    public async Task A_daemon_info_response_round_trips_each_watched_working_copy()
    {
        var response = new DaemonInfoResponse(
            "1.2.3",
            61.5,
            [new WatchedWorkingCopy("/wc", 20201, WatcherState.Recovering)]
        );

        var decoded = (DaemonInfoResponse)
            ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(response));

        await Assert.That(decoded.Version).IsEqualTo("1.2.3");
        await Assert.That(decoded.UptimeSeconds).IsEqualTo(61.5);
        await Assert.That(decoded.WorkingCopies).IsEquivalentTo(response.WorkingCopies);
    }

    [Test]
    [Arguments(20)]
    [Arguments(null)]
    public async Task A_log_request_round_trips_its_limit_including_the_absence_of_one(int? limit)
    {
        var request = new LogRequest("/wc/art", limit);

        await Assert
            .That(ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(request)))
            .IsEqualTo(request);
    }

    [Test]
    public async Task A_diff_request_round_trips_its_path()
    {
        var request = new DiffRequest("/wc/art");

        await Assert
            .That(ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(request)))
            .IsEqualTo(request);
    }

    /// <summary>
    /// The three that write travel as a list of paths. A list that came back empty, reordered or
    /// short would commit or revert something other than what the user named.
    /// </summary>
    [Test]
    public async Task A_request_that_writes_round_trips_every_path_it_named_in_order()
    {
        IReadOnlyList<string> paths = ["/wc/art/hero.png", "/wc/src/a.txt"];

        var add = (AddRequest)
            ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(new AddRequest(paths)));
        var revert = (RevertRequest)
            ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(new RevertRequest(paths)));
        var commit = (CommitRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(new CommitRequest(paths, "re-export hero"))
            );

        await Assert.That(add.Paths).IsEquivalentTo(paths);
        await Assert.That(revert.Paths).IsEquivalentTo(paths);
        await Assert.That(commit.Paths).IsEquivalentTo(paths);
        await Assert.That(commit.Message).IsEqualTo("re-export hero");
    }

    /// <summary>
    /// A log message is the one field a user types freely. Newlines and quotes in it must survive
    /// the wire, or a commit message arrives at the server mangled.
    /// </summary>
    [Test]
    public async Task A_commit_message_survives_newlines_and_quotes()
    {
        var message = "re-export \"hero\"\n\nwith the new rig";

        var decoded = (CommitRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(new CommitRequest(["/wc"], message))
            );

        await Assert.That(decoded.Message).IsEqualTo(message);
    }

    /// <summary>
    /// The scope is what tells a picked set from a subtree, and a commit that arrived with the
    /// wrong one would send changes nobody approved. Both values are here, and the default is one
    /// of them on purpose: a front-end that does not know about scopes gets SVN's own behaviour.
    /// </summary>
    [Test]
    [Arguments(CommitScope.WholeSubtree)]
    [Arguments(CommitScope.ExactlyTheseNodes)]
    public async Task A_commit_request_round_trips_the_scope_it_was_given(CommitScope scope)
    {
        var decoded = (CommitRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(new CommitRequest(["/wc"], "m", scope))
            );

        await Assert.That(decoded.Scope).IsEqualTo(scope);
    }

    [Test]
    public async Task A_commit_request_that_names_no_scope_means_the_whole_subtree()
    {
        var decoded = (CommitRequest)
            ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(new CommitRequest(["/wc"], "m")));

        await Assert.That(decoded.Scope).IsEqualTo(CommitScope.WholeSubtree);
    }

    [Test]
    [Arguments(42)]
    [Arguments(null)]
    public async Task A_commit_response_round_trips_its_revision_including_the_absence_of_one(
        int? revision
    )
    {
        var decoded = (CommitResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(new CommitResponse(revision, "Sending a.txt\n"))
            );

        await Assert.That(decoded.Revision).IsEqualTo((long?)revision);
        await Assert.That(decoded.Notifications).IsEqualTo("Sending a.txt\n");
    }

    [Test]
    public async Task An_update_request_round_trips_its_single_path()
    {
        var decoded = (UpdateRequest)
            ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(new UpdateRequest("/wc/art")));

        await Assert.That(decoded.Path).IsEqualTo("/wc/art");
    }

    /// <summary>
    /// The two counts are the only thing that tells a clean update from one that left conflict
    /// markers on disk — SVN exits zero either way — so losing them on the wire would turn a
    /// working copy needing attention into a silent success.
    /// </summary>
    [Test]
    [Arguments(42)]
    [Arguments(null)]
    public async Task An_update_response_round_trips_its_revision_and_both_counts(int? revision)
    {
        var decoded = (UpdateResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(
                    new UpdateResponse(revision, Conflicts: 2, SkippedPaths: 1, "U    a.txt\n")
                )
            );

        await Assert.That(decoded.Revision).IsEqualTo((long?)revision);
        await Assert.That(decoded.Conflicts).IsEqualTo(2);
        await Assert.That(decoded.SkippedPaths).IsEqualTo(1);
        await Assert.That(decoded.Notifications).IsEqualTo("U    a.txt\n");
    }

    /// <summary>
    /// Stealing is the field that must never arrive by accident. A lock request whose
    /// <see cref="ForeignLock"/> went missing on the wire and defaulted the other way would take a
    /// teammate's lock off them without anybody having typed <c>--steal</c>.
    /// </summary>
    [Test]
    [Arguments(ForeignLock.Respected)]
    [Arguments(ForeignLock.Overridden)]
    public async Task A_lock_request_round_trips_whether_somebody_elses_lock_may_be_taken(
        ForeignLock foreign
    )
    {
        IReadOnlyList<string> paths = ["/wc/art/hero.png", "/wc/src/a.txt"];

        var take = (LockRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(new LockRequest(paths, "retouching", foreign))
            );
        var release = (UnlockRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(new UnlockRequest(paths, foreign))
            );

        await Assert.That(take.Paths).IsEquivalentTo(paths);
        await Assert.That(take.Comment).IsEqualTo("retouching");
        await Assert.That(take.Foreign).IsEqualTo(foreign);
        await Assert.That(release.Paths).IsEquivalentTo(paths);
        await Assert.That(release.Foreign).IsEqualTo(foreign);
    }

    [Test]
    public async Task A_lock_request_that_says_nothing_leaves_other_holders_alone_and_has_no_comment()
    {
        var decoded = (LockRequest)
            ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(new LockRequest(["/wc"], null)));

        await Assert.That(decoded.Comment).IsNull();
        await Assert.That(decoded.Foreign).IsEqualTo(ForeignLock.Respected);
        await Assert
            .That(
                (
                    (UnlockRequest)
                        ProtocolMessage.DecodeRequest(
                            ProtocolMessage.Encode(new UnlockRequest(["/wc"]))
                        )
                ).Foreign
            )
            .IsEqualTo(ForeignLock.Respected);
    }

    /// <summary>
    /// The refusals are the only thing that tells a granted lock from a refused one — <c>svn
    /// lock</c> exits zero for both — so losing them on the wire sends an artist to work on a file
    /// somebody else is holding.
    /// </summary>
    [Test]
    public async Task A_lock_response_round_trips_every_refusal_it_carries()
    {
        IReadOnlyList<string> refusals =
        [
            "svn: warning: W160035: Path '/art/hero.png' is already locked by user 'ada'",
            "svn: warning: W160042: Lock failed: newer version of '/src/a.txt' exists",
        ];

        var locked = (LockResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(new LockResponse("'x' locked by user 'bob'.\n", refusals))
            );
        var unlocked = (UnlockResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(new UnlockResponse("'x' unlocked.\n", refusals))
            );

        await Assert.That(locked.Notifications).IsEqualTo("'x' locked by user 'bob'.\n");
        await Assert.That(locked.Refusals).IsEquivalentTo(refusals);
        await Assert.That(unlocked.Notifications).IsEqualTo("'x' unlocked.\n");
        await Assert.That(unlocked.Refusals).IsEquivalentTo(refusals);
    }

    [Test]
    public async Task A_lock_response_with_nothing_refused_arrives_empty_rather_than_absent()
    {
        var decoded = (LockResponse)
            ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(new LockResponse("x", [])));

        await Assert.That(decoded.Refusals).IsEmpty();
    }

    [Test]
    public async Task The_locking_kinds_are_named_on_the_wire()
    {
        await Assert.That(Json(new LockRequest(["/wc"], null))).Contains("\"$kind\":\"lock\"");
        await Assert.That(Json(new UnlockRequest(["/wc"]))).Contains("\"$kind\":\"unlock\"");
        await Assert.That(Json(new LockResponse("", []))).Contains("\"$kind\":\"lock\"");
        await Assert.That(Json(new UnlockResponse("", []))).Contains("\"$kind\":\"unlock\"");
    }

    /// <summary>
    /// Which version was asked for is the whole content of the request: the same paths with a
    /// different one of these overwrites the opposite side of somebody's conflict.
    /// </summary>
    [Test]
    [Arguments(ConflictResolution.Working)]
    [Arguments(ConflictResolution.Mine)]
    [Arguments(ConflictResolution.Theirs)]
    [Arguments(ConflictResolution.Base)]
    public async Task A_resolve_request_round_trips_the_version_it_asks_to_keep(
        ConflictResolution resolution
    )
    {
        IReadOnlyList<string> paths = ["/wc/art/hero.png", "/wc/src/a.txt"];

        var decoded = (ResolveRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(new ResolveRequest(paths, resolution))
            );

        await Assert.That(decoded.Paths).IsEquivalentTo(paths);
        await Assert.That(decoded.Resolution).IsEqualTo(resolution);
    }

    /// <summary>
    /// Both halves matter and for opposite reasons: the resolved paths are the only proof anything
    /// happened, and the refusals are the only proof something did not.
    /// </summary>
    [Test]
    public async Task A_resolve_response_round_trips_what_was_settled_and_what_was_not()
    {
        IReadOnlyList<string> resolved = ["art/hero.png", "src/a.txt"];
        IReadOnlyList<string> refusals =
        [
            "svn: warning: W155027: Tree conflict can only be resolved to 'working' state",
        ];

        var decoded = (ResolveResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(new ResolveResponse(resolved, refusals))
            );

        await Assert.That(decoded.ResolvedPaths).IsEquivalentTo(resolved);
        await Assert.That(decoded.Refusals).IsEquivalentTo(refusals);
    }

    /// <summary>
    /// Nothing conflicted is a real answer, and it has to arrive as an empty list rather than as
    /// null — a front-end counting it is what turns silence into "nothing was conflicted".
    /// </summary>
    [Test]
    public async Task A_resolve_response_that_settled_nothing_arrives_empty_rather_than_absent()
    {
        var decoded = (ResolveResponse)
            ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(new ResolveResponse([], [])));

        await Assert.That(decoded.ResolvedPaths).IsEmpty();
        await Assert.That(decoded.Refusals).IsEmpty();
    }

    [Test]
    public async Task A_cleanup_request_round_trips_the_path_that_picks_the_working_copy()
    {
        var decoded = (CleanupRequest)
            ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(new CleanupRequest("/wc/art")));

        await Assert.That(decoded.Path).IsEqualTo("/wc/art");
    }

    /// <summary>
    /// All three halves matter, because <c>svn cleanup</c> says nothing at all: what was released is
    /// the only proof it worked, what was finished is the only proof the copy was unreadable, and
    /// what remains is the only proof it did not work.
    /// </summary>
    [Test]
    public async Task A_cleanup_response_round_trips_what_it_released_finished_and_left()
    {
        IReadOnlyList<string> released = ["", "art"];
        IReadOnlyList<string> remaining = ["src"];

        var decoded = (CleanupResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(new CleanupResponse(released, 4, remaining))
            );

        await Assert.That(decoded.ReleasedWriteLocks).IsEquivalentTo(released);
        await Assert.That(decoded.FinishedOperations).IsEqualTo(4);
        await Assert.That(decoded.RemainingWriteLocks).IsEquivalentTo(remaining);
    }

    /// <summary>
    /// The root's relative path is the empty string, and the row a crashed client leaves is the
    /// root's — so the one value this response most often carries is the one JSON is likeliest to
    /// lose.
    /// </summary>
    [Test]
    public async Task A_cleanup_response_keeps_the_empty_relative_path_that_means_the_root()
    {
        var decoded = (CleanupResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(new CleanupResponse([string.Empty], 0, []))
            );

        await Assert.That(decoded.ReleasedWriteLocks).IsEquivalentTo([string.Empty]);
    }

    [Test]
    public async Task A_cleanup_response_that_did_nothing_arrives_empty_rather_than_absent()
    {
        var decoded = (CleanupResponse)
            ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(new CleanupResponse([], 0, [])));

        await Assert.That(decoded.ReleasedWriteLocks).IsEmpty();
        await Assert.That(decoded.FinishedOperations).IsEqualTo(0);
        await Assert.That(decoded.RemainingWriteLocks).IsEmpty();
    }

    [Test]
    public async Task The_cleanup_kinds_are_named_on_the_wire()
    {
        await Assert.That(Json(new CleanupRequest("/wc"))).Contains("\"$kind\":\"cleanup\"");
        await Assert.That(Json(new CleanupResponse([], 0, []))).Contains("\"$kind\":\"cleanup\"");
    }

    [Test]
    public async Task The_resolve_kinds_are_named_on_the_wire()
    {
        await Assert
            .That(Json(new ResolveRequest(["/wc"], ConflictResolution.Mine)))
            .Contains("\"$kind\":\"resolve\"");
        await Assert.That(Json(new ResolveResponse([], []))).Contains("\"$kind\":\"resolve\"");
    }

    /// <summary>
    /// The enum travels as its name, not as an ordinal — adding a member in the middle would
    /// otherwise silently re-point every older client at a different side of the conflict.
    /// </summary>
    [Test]
    public async Task The_version_to_keep_travels_as_a_word()
    {
        await Assert
            .That(Json(new ResolveRequest(["/wc"], ConflictResolution.Theirs)))
            .Contains("\"Theirs\"");
    }

    [Test]
    public async Task A_notification_response_round_trips_svns_text_unchanged()
    {
        var notifications = "A         art/hero.png\nA         art/hero.psd\n";

        var add = (AddResponse)
            ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(new AddResponse(notifications)));
        var revert = (RevertResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(new RevertResponse(notifications))
            );

        await Assert.That(add.Notifications).IsEqualTo(notifications);
        await Assert.That(revert.Notifications).IsEqualTo(notifications);
    }

    [Test]
    public async Task A_move_request_round_trips_both_of_its_paths_the_right_way_round()
    {
        var decoded = (MoveRequest)
            ProtocolMessage.DecodeRequest(
                ProtocolMessage.Encode(
                    new MoveRequest("/wc/art/hero.png", "/wc/art/protagonist.png")
                )
            );

        await Assert.That(decoded.Source).IsEqualTo("/wc/art/hero.png");
        await Assert.That(decoded.Destination).IsEqualTo("/wc/art/protagonist.png");
    }

    /// <summary>
    /// The route is what tells a front-end it recorded a rename somebody had already made, rather
    /// than making one. It travels as its name, so a daemon and a CLI built a week apart agree.
    /// </summary>
    [Test]
    [Arguments(MoveRoute.Ordinary)]
    [Arguments(MoveRoute.AlreadyRenamed)]
    public async Task A_move_response_round_trips_its_route(MoveRoute route)
    {
        var decoded = (MoveResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(new MoveResponse(route, "A         art/protagonist.png\n"))
            );

        await Assert.That(decoded.Route).IsEqualTo(route);
        await Assert.That(decoded.Notifications).IsEqualTo("A         art/protagonist.png\n");
    }

    [Test]
    public async Task A_move_route_travels_as_its_name_and_not_as_a_number()
    {
        await Assert
            .That(Json(new MoveResponse(MoveRoute.AlreadyRenamed, "")))
            .Contains("\"AlreadyRenamed\"");
    }

    [Test]
    public async Task A_delete_request_round_trips_every_path_it_named_in_order()
    {
        IReadOnlyList<string> paths = ["/wc/art/hero.png", "/wc/src/a.txt"];

        var decoded = (DeleteRequest)
            ProtocolMessage.DecodeRequest(ProtocolMessage.Encode(new DeleteRequest(paths)));

        await Assert.That(decoded.Paths).IsEquivalentTo(paths);
    }

    /// <summary>
    /// A rename SVN was never told about is a fact about a pair, not about either node, so it rides
    /// the response rather than an entry. Both halves have to survive, and the right way round.
    /// </summary>
    [Test]
    public async Task A_status_response_round_trips_the_renames_it_paired()
    {
        var decoded = (StatusResponse)
            ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(SampleStatusResponse()));

        await Assert.That(decoded.UnrecordedMoves.Count).IsEqualTo(1);
        await Assert.That(decoded.UnrecordedMoves[0].FromRelPath).IsEqualTo("art/hero.png");
        await Assert.That(decoded.UnrecordedMoves[0].ToRelPath).IsEqualTo("art/protagonist.png");
    }

    [Test]
    public async Task A_status_response_with_no_renames_round_trips_an_empty_list()
    {
        var decoded = (StatusResponse)
            ProtocolMessage.DecodeResponse(
                ProtocolMessage.Encode(SampleStatusResponse() with { UnrecordedMoves = [] })
            );

        await Assert.That(decoded.UnrecordedMoves).IsEmpty();
    }

    /// <summary>
    /// The discriminator is the contract between a daemon left running since yesterday and a
    /// front-end upgraded this morning; these four are the ones that change a working copy.
    /// </summary>
    [Test]
    public async Task The_kinds_that_write_are_named_on_the_wire()
    {
        await Assert.That(Json(new MoveRequest("/wc/a", "/wc/b"))).Contains("\"$kind\":\"move\"");
        await Assert
            .That(Json(new MoveResponse(MoveRoute.Ordinary, "")))
            .Contains("\"$kind\":\"move\"");
        await Assert.That(Json(new DeleteRequest(["/wc"]))).Contains("\"$kind\":\"delete\"");
        await Assert.That(Json(new DeleteResponse(""))).Contains("\"$kind\":\"delete\"");
        await Assert.That(Json(new UpdateRequest("/wc"))).Contains("\"$kind\":\"update\"");
        await Assert.That(Json(new UpdateResponse(1, 0, 0, ""))).Contains("\"$kind\":\"update\"");
        await Assert.That(Json(new AddRequest(["/wc"]))).Contains("\"$kind\":\"add\"");
        await Assert.That(Json(new RevertRequest(["/wc"]))).Contains("\"$kind\":\"revert\"");
        await Assert.That(Json(new CommitRequest(["/wc"], "m"))).Contains("\"$kind\":\"commit\"");
        await Assert.That(Json(new AddResponse(""))).Contains("\"$kind\":\"add\"");
        await Assert.That(Json(new RevertResponse(""))).Contains("\"$kind\":\"revert\"");
        await Assert.That(Json(new CommitResponse(1, ""))).Contains("\"$kind\":\"commit\"");
    }

    [Test]
    public async Task A_log_response_round_trips_a_revision_whole()
    {
        var response = new LogResponse([SampleRevision]);

        var decoded = (LogResponse)ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(response));

        await Assert.That(decoded.Revisions.Count).IsEqualTo(1);
        await Assert.That(decoded.Revisions[0].Revision).IsEqualTo(42L);
        await Assert.That(decoded.Revisions[0].Author).IsEqualTo("artist");
        await Assert.That(decoded.Revisions[0].Date).IsEqualTo(SampleRevision.Date);
        await Assert.That(decoded.Revisions[0].Message).IsEqualTo("re-export\nhero");
        await Assert
            .That(decoded.Revisions[0].ChangedPaths)
            .IsEquivalentTo(SampleRevision.ChangedPaths);
    }

    /// <summary>
    /// An anonymous commit and a copy that came from nowhere both travel as absent. Either coming
    /// back as a default would put an author on a commit that has none.
    /// </summary>
    [Test]
    public async Task Absent_author_date_and_copy_source_stay_absent()
    {
        var response = new LogResponse([
            new RevisionEntry(
                1,
                Author: null,
                Date: null,
                Message: string.Empty,
                [new ChangedPath("/src", PathChange.Added, null, null)]
            ),
        ]);

        var decoded = (LogResponse)ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(response));

        await Assert.That(decoded.Revisions[0].Author).IsNull();
        await Assert.That(decoded.Revisions[0].Date).IsNull();
        await Assert.That(decoded.Revisions[0].ChangedPaths[0].CopiedFromPath).IsNull();
        await Assert.That(decoded.Revisions[0].ChangedPaths[0].CopiedFromRevision).IsNull();
    }

    /// <summary>
    /// SVN's diff is the one payload we do not parse, so every byte of it has to survive the wire
    /// — tabs and blank lines included, because a diff is whitespace-significant.
    /// </summary>
    [Test]
    public async Task A_diff_response_round_trips_svns_text_byte_for_byte()
    {
        var diff = "Index: a\n--- a\t(revision 1)\n+++ a\t(working copy)\n@@ -1 +1 @@\n-x\n+y\n\n";

        var decoded = (DiffResponse)
            ProtocolMessage.DecodeResponse(ProtocolMessage.Encode(new DiffResponse(diff)));

        await Assert.That(decoded.UnifiedDiff).IsEqualTo(diff);
    }

    [Test]
    public async Task The_log_and_diff_kinds_are_named_on_the_wire()
    {
        await Assert.That(Json(new LogRequest("/wc", 20))).Contains("\"$kind\":\"log\"");
        await Assert.That(Json(new DiffRequest("/wc"))).Contains("\"$kind\":\"diff\"");
        await Assert.That(Json(new LogResponse([]))).Contains("\"$kind\":\"log\"");
        await Assert.That(Json(new DiffResponse(""))).Contains("\"$kind\":\"diff\"");
    }

    [Test]
    public async Task A_path_change_travels_as_its_name_and_not_as_an_ordinal()
    {
        await Assert.That(Json(new LogResponse([SampleRevision]))).Contains("\"Modified\"");
        await Assert
            .That(Json(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "x")))
            .Contains("\"SvnCommandFailed\"");
    }

    [Test]
    public async Task A_payload_that_is_not_json_is_refused_as_a_protocol_error()
    {
        await Assert
            .That(() => ProtocolMessage.DecodeRequest("{"u8))
            .Throws<ProtocolException>()
            .WithMessageContaining("request");
    }

    /// <summary>
    /// A newer front-end talking to an older daemon. Refusing is the only honest answer; guessing
    /// at the nearest known kind would carry out something the caller did not ask for.
    /// </summary>
    [Test]
    public async Task A_kind_this_build_has_never_heard_of_is_refused()
    {
        await Assert
            .That(() => ProtocolMessage.DecodeRequest("""{"$kind":"teleport"}"""u8))
            .Throws<ProtocolException>();
    }

    [Test]
    public async Task A_message_with_no_kind_at_all_is_refused()
    {
        await Assert
            .That(() => ProtocolMessage.DecodeRequest("""{"workingCopyPath":"/wc"}"""u8))
            .Throws<ProtocolException>();
    }

    [Test]
    public async Task A_literal_json_null_is_refused_rather_than_returned()
    {
        await Assert
            .That(() => ProtocolMessage.DecodeResponse("null"u8))
            .Throws<ProtocolException>();
    }

    [Test]
    public async Task A_malformed_response_says_response_and_not_request()
    {
        await Assert
            .That(() => ProtocolMessage.DecodeResponse("["u8))
            .Throws<ProtocolException>()
            .WithMessageContaining("response");
    }

    private static readonly RevisionEntry SampleRevision = new(
        42,
        "artist",
        new DateTimeOffset(2026, 9, 19, 10, 30, 0, TimeSpan.Zero),
        "re-export\nhero",
        [
            new ChangedPath("/art/hero.png", PathChange.Modified, null, null),
            new ChangedPath("/branches/x", PathChange.Added, "/trunk", 41),
        ]
    );

    private static readonly WorkingCopyInfo SampleInfo = new(
        "/wc",
        "https://svn.example/repo",
        "6e1a8f1a-0000-0000-0000-000000000000",
        31
    );

    private static readonly WorkingCopyEntry SampleEntry = new(
        "art/hero.png",
        NodeKind.File,
        NodeStatus.Modified,
        PropertyStatus.Modified,
        Revision: 42,
        Changelist: "assets",
        IsConflicted: true,
        HasLockToken: true,
        IsWriteLocked: false,
        IsCopied: true
    );

    private static StatusResponse SampleStatusResponse() =>
        new(
            SampleInfo,
            [SampleEntry],
            ServedFromWarmIndex: true,
            ServerElapsedMilliseconds: 1.5,
            UnfinishedOperations: 3,
            UnrecordedMoves: [new UnrecordedMove("art/hero.png", "art/protagonist.png")]
        );

    private static string Json(DaemonRequest request) =>
        Encoding.UTF8.GetString(ProtocolMessage.Encode(request));

    private static string Json(DaemonResponse response) =>
        Encoding.UTF8.GetString(ProtocolMessage.Encode(response));
}
