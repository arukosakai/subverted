using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Subverted.Core;
using Subverted.Protocol;
using Subverted.Svn;
using Subverted.Svn.Tests;

namespace Subverted.Daemon.Tests;

/// <summary>
/// The whole chain at once: a real working copy read through a real wc.db, a real filesystem
/// watch, a real Unix domain socket and a real client on the far end. Every test above this one
/// hands some part of the chain a shape that a test invented — this is the one that does not.
/// </summary>
public sealed class DaemonEndToEndTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly TimeSpan WatcherPatience = TimeSpan.FromSeconds(15);

    [Test]
    public async Task A_status_request_over_the_socket_reports_what_changed_on_disk()
    {
        using var copy = Committed(("art/hero.png", "pixels"), ("readme.txt", "hello"));
        copy.Write("art/hero.png", "different pixels entirely");

        await WithDaemon(
            copy,
            async client =>
            {
                var status = await StatusAsync(client, copy.Root);

                await Assert
                    .That(Path.GetFullPath(status.Info.RootPath))
                    .IsEqualTo(Path.GetFullPath(copy.Root));
                await Assert.That(status.Entries.Count).IsEqualTo(1);
                await Assert.That(status.Entries[0].RelPath).IsEqualTo("art/hero.png");
                await Assert.That(status.Entries[0].Status).IsEqualTo(NodeStatus.Modified);
            }
        );
    }

    /// <summary>
    /// The premise of the whole milestone. The first request pays for the tree walk and the second
    /// one does not, without anything on disk having changed in between.
    /// </summary>
    [Test]
    public async Task The_second_request_is_answered_from_memory()
    {
        using var copy = Committed(("readme.txt", "hello"));

        await WithDaemon(
            copy,
            async client =>
            {
                var first = await StatusAsync(client, copy.Root);
                var second = await StatusAsync(client, copy.Root);

                await Assert.That(first.ServedFromWarmIndex).IsFalse();
                await Assert.That(second.ServedFromWarmIndex).IsTrue();
            }
        );
    }

    /// <summary>
    /// D5's whole reason for existing. A daemon that holds an index and does not notice a write is
    /// worse than no daemon, because it is confidently wrong rather than slow.
    /// </summary>
    [Test]
    public async Task A_file_written_after_the_index_was_warm_still_shows_up()
    {
        using var copy = Committed(("readme.txt", "hello"));

        await WithDaemon(
            copy,
            async client =>
            {
                await StatusAsync(client, copy.Root);

                copy.Write("written-later.txt", "the daemon was already holding this tree");

                await Assert
                    .That(await NoticedAsync(client, copy.Root, "written-later.txt"))
                    .IsTrue();
            }
        );
    }

    [Test]
    public async Task An_edit_to_a_versioned_file_after_the_index_was_warm_still_shows_up()
    {
        using var copy = Committed(("readme.txt", "hello"));

        await WithDaemon(
            copy,
            async client =>
            {
                var first = await StatusAsync(client, copy.Root);
                await Assert.That(first.Entries).IsEmpty();

                copy.Write("readme.txt", "hello, and then some more text so the size moves");

                await Assert.That(await NoticedAsync(client, copy.Root, "readme.txt")).IsTrue();
            }
        );
    }

    [Test]
    public async Task A_path_in_no_working_copy_is_a_named_error_and_not_a_crash()
    {
        using var copy = Committed(("readme.txt", "hello"));
        var outside = Path.GetTempPath();

        await WithDaemon(
            copy,
            async client =>
            {
                var response = await client.SendAsync(
                    new StatusRequest(outside, IncludeUnmodified: false, IncludeIgnored: false),
                    None
                );

                await Assert.That(response).IsTypeOf<ErrorResponse>();
                await Assert
                    .That(((ErrorResponse)response).Kind)
                    .IsEqualTo(DaemonErrorKind.NotAWorkingCopy);
            }
        );
    }

    [Test]
    public async Task Daemon_info_reports_the_working_copy_it_picked_up_and_that_it_is_watching()
    {
        using var copy = Committed(("readme.txt", "hello"));

        await WithDaemon(
            copy,
            async client =>
            {
                await StatusAsync(client, copy.Root);

                var info = (DaemonInfoResponse)
                    await client.SendAsync(new DaemonInfoRequest(), None);

                await Assert.That(info.WorkingCopies.Count).IsEqualTo(1);
                await Assert.That(info.WorkingCopies[0].EntryCount).IsGreaterThan(0);
                await Assert
                    .That(info.WorkingCopies[0].WatcherState)
                    .IsEqualTo(WatcherState.Healthy);
            }
        );
    }

    /// <summary>
    /// The switch has to survive the round trip, not just the filter's unit test: an ignored file
    /// is absent by default and present when asked for, from the same warm index.
    /// </summary>
    [Test]
    public async Task Ignored_nodes_arrive_only_when_the_request_asked_for_them()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Svn("propset", "svn:ignore", "*.tmp", ".");
        copy.Svn("commit", "--quiet", "-m", "ignore tmp");
        copy.Write("scratch.tmp", "build output");

        await WithDaemon(
            copy,
            async client =>
            {
                var withoutIgnored = await StatusAsync(client, copy.Root);
                var withIgnored = (StatusResponse)
                    await client.SendAsync(
                        new StatusRequest(
                            copy.Root,
                            IncludeUnmodified: false,
                            IncludeIgnored: true
                        ),
                        None
                    );

                await Assert
                    .That(withoutIgnored.Entries.Any(entry => entry.RelPath == "scratch.tmp"))
                    .IsFalse();
                await Assert
                    .That(
                        withIgnored.Entries.Single(entry => entry.RelPath == "scratch.tmp").Status
                    )
                    .IsEqualTo(NodeStatus.Ignored);
            }
        );
    }

    /// <summary>
    /// Against a repository that really has two revisions in it. The XML parser is the part this
    /// proves: a hand-written sample can only confirm it matches our idea of SVN's output.
    /// </summary>
    [Test]
    public async Task A_log_request_comes_back_with_the_revisions_the_repository_really_has()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("readme.txt", "hello again");
        copy.Svn("commit", "--quiet", "-m", "the second commit");

        // `svn log` on a working-copy path stops at that path's BASE revision, and committing a
        // file leaves the directory holding it a revision behind. Without this the second commit
        // is in the repository and invisible to the log.
        copy.Svn("update", "--quiet");

        await WithDaemon(
            copy,
            async client =>
            {
                var response = await client.SendAsync(new LogRequest(copy.Root, Limit: null), None);

                await Assert.That(response).IsTypeOf<LogResponse>();
                var revisions = ((LogResponse)response).Revisions;
                await Assert.That(revisions.Count).IsEqualTo(2);
                await Assert.That(revisions[0].Revision).IsEqualTo(2L);
                await Assert.That(revisions[0].Message).IsEqualTo("the second commit");
                await Assert.That(revisions[1].Revision).IsEqualTo(1L);
                await Assert.That(revisions[0].Date).IsNotNull();
            }
        );
    }

    /// <summary>Newest first, and the limit is honoured — an artist asking for history wants the end of it.</summary>
    [Test]
    public async Task A_log_limit_takes_the_newest_revisions_and_not_the_oldest()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("readme.txt", "hello again");
        copy.Svn("commit", "--quiet", "-m", "second");
        copy.Svn("update", "--quiet");

        await WithDaemon(
            copy,
            async client =>
            {
                var response = (LogResponse)
                    await client.SendAsync(new LogRequest(copy.Root, Limit: 1), None);

                await Assert.That(response.Revisions.Count).IsEqualTo(1);
                await Assert.That(response.Revisions[0].Revision).IsEqualTo(2L);
            }
        );
    }

    [Test]
    public async Task A_diff_request_comes_back_with_svns_own_text_for_a_real_edit()
    {
        using var copy = Committed(("readme.txt", "hello\n"));
        copy.Write("readme.txt", "hello\nand a second line\n");

        await WithDaemon(
            copy,
            async client =>
            {
                var response = await client.SendAsync(new DiffRequest(copy.Root), None);

                await Assert.That(response).IsTypeOf<DiffResponse>();
                var diff = ((DiffResponse)response).UnifiedDiff;
                await Assert.That(diff).Contains("readme.txt");
                await Assert.That(diff).Contains("+and a second line");
            }
        );
    }

    [Test]
    public async Task A_diff_of_a_working_copy_with_nothing_changed_is_empty()
    {
        using var copy = Committed(("readme.txt", "hello\n"));

        await WithDaemon(
            copy,
            async client =>
            {
                var response = (DiffResponse)
                    await client.SendAsync(new DiffRequest(copy.Root), None);

                await Assert.That(response.UnifiedDiff).IsEmpty();
            }
        );
    }

    /// <summary>
    /// What <c>sv pick</c> sends, over the real socket to the real client: the nodes that were
    /// picked and nothing below them. The sibling left out has to still be there afterwards, and
    /// the daemon has to say so from its own index rather than from a rescan somebody triggered.
    /// </summary>
    [Test]
    public async Task A_picked_set_commits_exactly_those_nodes_and_leaves_the_rest_local()
    {
        using var copy = Committed(("art/hero.png", "pixels"), ("art/villain.png", "more pixels"));
        copy.Write("art/hero.png", "re-exported");
        copy.Write("art/villain.png", "also re-exported");

        await WithDaemon(
            copy,
            async client =>
            {
                var response = await client.SendAsync(
                    new CommitRequest(
                        [copy.Absolute("art/hero.png")],
                        "just the hero",
                        CommitScope.ExactlyTheseNodes
                    ),
                    None
                );

                await Assert.That(response).IsTypeOf<CommitResponse>();
                await Assert.That(((CommitResponse)response).Revision).IsEqualTo(2L);

                var after = await StatusAsync(client, copy.Root);
                await Assert
                    .That(after.Entries.Select(entry => entry.RelPath))
                    .IsEquivalentTo(["art/villain.png"]);
            }
        );
    }

    /// <summary>
    /// The other half of the scope, over the same chain: a directory named without the picker's
    /// scope takes everything under it. One without the other proves nothing about which reached
    /// the client.
    /// </summary>
    [Test]
    public async Task The_same_directory_committed_as_a_subtree_takes_everything_under_it()
    {
        using var copy = Committed(("art/hero.png", "pixels"), ("art/villain.png", "more pixels"));
        copy.Write("art/hero.png", "re-exported");
        copy.Write("art/villain.png", "also re-exported");

        await WithDaemon(
            copy,
            async client =>
            {
                await client.SendAsync(
                    new CommitRequest([copy.Absolute("art")], "the lot", CommitScope.WholeSubtree),
                    None
                );

                var after = await StatusAsync(client, copy.Root);
                await Assert.That(after.Entries).IsEmpty();
            }
        );
    }

    /// <summary>
    /// An update over the whole chain, with a second checkout standing in for a teammate. The
    /// status afterwards is the part that matters as much as the update: the daemon was holding an
    /// index of a tree that no longer exists, and it has to answer from the new one.
    /// </summary>
    [Test]
    public async Task An_update_brings_a_teammates_commit_in_and_the_daemon_answers_from_the_new_tree()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        using var teammate = copy.AnotherCheckout();
        teammate.Write("art/hero.png", "their pixels");
        teammate.Svn("commit", "--quiet", "-m", "their re-export");

        await WithDaemon(
            copy,
            async client =>
            {
                var warm = await StatusAsync(client, copy.Root);

                var response = await client.SendAsync(new UpdateRequest(copy.Root), None);

                await Assert.That(response).IsTypeOf<UpdateResponse>();
                await Assert.That(((UpdateResponse)response).Revision).IsEqualTo(2L);
                await Assert.That(((UpdateResponse)response).Conflicts).IsEqualTo(0);
                await Assert
                    .That(File.ReadAllText(copy.Absolute("art/hero.png")))
                    .IsEqualTo("their pixels");

                await Assert.That(warm.Entries).IsEmpty();
                var after = await StatusAsync(client, copy.Root);
                await Assert.That(after.Entries).IsEmpty();
            }
        );
    }

    /// <summary>
    /// The trap this command is built around, over the real chain: SVN exits zero on a conflict, so
    /// the daemon answers with an ordinary <see cref="UpdateResponse"/> and the count is the only
    /// thing saying the file on disk now holds merge markers.
    /// </summary>
    [Test]
    public async Task A_conflicted_update_comes_back_as_a_success_carrying_the_count()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        using var teammate = copy.AnotherCheckout();
        teammate.Write("art/hero.png", "their pixels");
        teammate.Svn("commit", "--quiet", "-m", "their re-export");
        copy.Write("art/hero.png", "my pixels");

        await WithDaemon(
            copy,
            async client =>
            {
                var response = await client.SendAsync(new UpdateRequest(copy.Root), None);

                await Assert.That(response).IsTypeOf<UpdateResponse>();
                await Assert.That(((UpdateResponse)response).Conflicts).IsEqualTo(1);

                var after = await StatusAsync(client, copy.Root);
                await Assert
                    .That(
                        after.Entries.Single(entry => entry.RelPath == "art/hero.png").IsConflicted
                    )
                    .IsTrue();
            }
        );
    }

    /// <summary>
    /// The whole chain for a lock: the client asks, <c>svn</c> takes it, the daemon drops its index,
    /// and the next <c>sv st</c> shows the <c>K</c> that came out of wc.db rather than a stale
    /// answer taken before the lock existed.
    /// </summary>
    [Test]
    public async Task A_lock_is_taken_and_the_next_status_reports_the_working_copy_holding_it()
    {
        using var copy = Committed(("art/hero.png", "pixels"));

        await WithDaemon(
            copy,
            async client =>
            {
                await StatusAsync(client, copy.Root);

                var response = await client.SendAsync(
                    new LockRequest([copy.Absolute("art/hero.png")], "retouching"),
                    None
                );

                await Assert.That(response).IsTypeOf<LockResponse>();
                await Assert.That(((LockResponse)response).Refusals).IsEmpty();

                var after = await StatusAsync(client, copy.Root);
                await Assert
                    .That(
                        after.Entries.Single(entry => entry.RelPath == "art/hero.png").HasLockToken
                    )
                    .IsTrue();
            }
        );
    }

    /// <summary>
    /// The trap this command is built around, over the real chain: SVN warns and exits zero when
    /// somebody else holds the path, so the daemon answers with an ordinary
    /// <see cref="LockResponse"/> and the refusal is the only thing saying the lock was not granted.
    /// </summary>
    [Test]
    public async Task A_lock_somebody_else_holds_comes_back_as_a_success_carrying_the_refusal()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        using var teammate = copy.AnotherCheckout();
        teammate.Svn("lock", "art/hero.png");

        await WithDaemon(
            copy,
            async client =>
            {
                var response = await client.SendAsync(
                    new LockRequest([copy.Absolute("art/hero.png")], null),
                    None
                );

                await Assert.That(response).IsTypeOf<LockResponse>();
                await Assert.That(((LockResponse)response).Refusals.Count).IsEqualTo(1);
                await Assert.That(((LockResponse)response).Refusals[0]).Contains("already locked");

                var after = await StatusAsync(client, copy.Root);
                await Assert.That(after.Entries).IsEmpty();
            }
        );
    }

    [Test]
    public async Task An_unlock_gives_the_lock_back_and_the_next_status_stops_reporting_it()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Svn("lock", "art/hero.png");

        await WithDaemon(
            copy,
            async client =>
            {
                var held = await StatusAsync(client, copy.Root);

                var response = await client.SendAsync(
                    new UnlockRequest([copy.Absolute("art/hero.png")]),
                    None
                );

                await Assert.That(response).IsTypeOf<UnlockResponse>();
                await Assert.That(((UnlockResponse)response).Refusals).IsEmpty();
                await Assert.That(held.Entries.Single().HasLockToken).IsTrue();

                var after = await StatusAsync(client, copy.Root);
                await Assert.That(after.Entries).IsEmpty();
            }
        );
    }

    /// <summary>
    /// The one state in which <c>svn status</c> answers nothing at all — <c>E155037</c> — and
    /// Subverted answers anyway. Driven through the socket rather than a fake because the wiring
    /// that decides it is the factory asking whether the reader it opened can see a work queue,
    /// and every test that hands the session its own reader would pass with that cast deleted.
    /// </summary>
    [Test]
    public async Task A_working_copy_svn_refuses_to_read_is_reported_as_mid_operation()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Write("art/hero.png", "different pixels entirely");
        WorkingCopyWedge.QueueInterruptedWork(copy, "art/hero.png");

        await WithDaemon(
            copy,
            async client =>
            {
                var status = await StatusAsync(client, copy.Root);

                await Assert.That(status.UnfinishedOperations).IsEqualTo(1);
                await Assert.That(status.Entries.Count).IsEqualTo(1);
            }
        );
    }

    [Test]
    public async Task A_working_copy_svn_reads_happily_is_not_reported_as_mid_operation()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Write("art/hero.png", "different pixels entirely");

        await WithDaemon(
            copy,
            async client =>
            {
                var status = await StatusAsync(client, copy.Root);

                await Assert.That(status.UnfinishedOperations).IsEqualTo(0);
            }
        );
    }

    /// <summary>
    /// Through the socket, against a real checkout: the pairing is a capability the factory has to
    /// recognise on the wc.db reader and hand to the session. Every test that builds a session with
    /// a fake would pass with that cast deleted.
    /// </summary>
    [Test]
    public async Task A_rename_made_outside_svn_reaches_the_front_end_as_a_pair()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        File.Move(copy.Absolute("art/hero.png"), copy.Absolute("art/protagonist.png"));

        await WithDaemon(
            copy,
            async client =>
            {
                var status = await StatusAsync(client, copy.Root);

                await Assert.That(status.UnrecordedMoves.Count).IsEqualTo(1);
                await Assert.That(status.UnrecordedMoves[0].FromRelPath).IsEqualTo("art/hero.png");
                await Assert
                    .That(status.UnrecordedMoves[0].ToRelPath)
                    .IsEqualTo("art/protagonist.png");
            }
        );
    }

    [Test]
    public async Task A_working_copy_nobody_renamed_anything_in_reports_no_pairs()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Write("art/hero.png", "different pixels entirely");

        await WithDaemon(
            copy,
            async client =>
            {
                await Assert.That((await StatusAsync(client, copy.Root)).UnrecordedMoves).IsEmpty();
            }
        );
    }

    /// <summary>
    /// The whole point of the feature, end to end: <c>svn move</c> cannot record a rename its source
    /// has already left, and after this one the history is kept and the pair is gone.
    /// </summary>
    [Test]
    public async Task Renaming_after_the_fact_records_the_move_and_clears_the_pair()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        File.Move(copy.Absolute("art/hero.png"), copy.Absolute("art/protagonist.png"));

        await WithDaemon(
            copy,
            async client =>
            {
                var moved = await client.SendAsync(
                    new MoveRequest(
                        copy.Absolute("art/hero.png"),
                        copy.Absolute("art/protagonist.png")
                    ),
                    None
                );

                await Assert.That(moved).IsTypeOf<MoveResponse>();
                await Assert.That(((MoveResponse)moved).Route).IsEqualTo(MoveRoute.AlreadyRenamed);

                var status = await StatusAsync(client, copy.Root);
                await Assert.That(status.UnrecordedMoves).IsEmpty();
                await Assert
                    .That(
                        status
                            .Entries.Single(entry => entry.RelPath == "art/protagonist.png")
                            .Status
                    )
                    .IsEqualTo(NodeStatus.Added);
            }
        );
    }

    [Test]
    public async Task A_removal_takes_the_file_out_of_svn_and_off_disk()
    {
        using var copy = Committed(("art/hero.png", "pixels"));

        await WithDaemon(
            copy,
            async client =>
            {
                var removed = await client.SendAsync(
                    new DeleteRequest([copy.Absolute("art/hero.png")]),
                    None
                );

                await Assert.That(removed).IsTypeOf<DeleteResponse>();
                await Assert.That(File.Exists(copy.Absolute("art/hero.png"))).IsFalse();

                var status = await StatusAsync(client, copy.Root);
                await Assert
                    .That(status.Entries.Single(entry => entry.RelPath == "art/hero.png").Status)
                    .IsEqualTo(NodeStatus.Deleted);
            }
        );
    }

    /// <summary>
    /// The refusal travels as an error rather than as an empty success, and keeps its kind — a
    /// front-end maps that onto "you asked for something that cannot be done", not "retry".
    /// </summary>
    [Test]
    public async Task A_rename_onto_an_existing_file_comes_back_refused()
    {
        using var copy = Committed(("art/hero.png", "one"), ("art/villain.png", "two"));

        await WithDaemon(
            copy,
            async client =>
            {
                var response = await client.SendAsync(
                    new MoveRequest(
                        copy.Absolute("art/hero.png"),
                        copy.Absolute("art/villain.png")
                    ),
                    None
                );

                await Assert.That(response).IsTypeOf<ErrorResponse>();
                await Assert
                    .That(((ErrorResponse)response).Kind)
                    .IsEqualTo(DaemonErrorKind.RequestRefused);
                await Assert.That(File.ReadAllText(copy.Absolute("art/hero.png"))).IsEqualTo("one");
            }
        );
    }

    /// <summary>
    /// One working copy, two spellings: opened through one and asked about through the other. The
    /// app did exactly this — a recent-list path from <c>%TEMP%</c> against a root spelled long —
    /// and was told its own folder was "not in the working copy".
    /// </summary>
    [Test]
    [Arguments("long-then-short")]
    [Arguments("short-then-long")]
    public async Task A_working_copy_answers_to_both_spellings_of_its_path(string order)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Write("art/hero.png", "different pixels entirely");
        var (first, second) = Spellings(copy.Root, order);

        await WithDaemon(
            copy,
            async client =>
            {
                await StatusAsync(client, first);
                var status = await client.SendAsync(
                    new StatusRequest(second, false, false, Scope: [second]),
                    None
                );

                await Assert.That(status).IsTypeOf<StatusResponse>();
                await Assert
                    .That(((StatusResponse)status).Entries.Select(entry => entry.RelPath))
                    .IsEquivalentTo(new[] { "art/hero.png" });
            }
        );
    }

    /// <summary>
    /// A command that writes checks every target against the root it resolved, and hands SVN a
    /// path relative to that root; the other spelling must pass the check and produce that path.
    /// </summary>
    [Test]
    [Arguments("long-then-short")]
    [Arguments("short-then-long")]
    public async Task A_write_names_its_target_in_either_spelling(string order)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("art/new.png", "fresh");
        var (first, second) = Spellings(copy.Root, order);

        await WithDaemon(
            copy,
            async client =>
            {
                await StatusAsync(client, first);
                var added = await client.SendAsync(
                    new AddRequest([Path.Combine(second, "art", "new.png")]),
                    None
                );

                await Assert.That(added).IsTypeOf<AddResponse>();
                var status = await StatusAsync(client, first);
                await Assert
                    .That(status.Entries.Single(entry => entry.RelPath == "art/new.png").Status)
                    .IsEqualTo(NodeStatus.Added);
            }
        );
    }

    /// <summary>The two spellings of a real temp folder; they differ on a machine with 8.3 names.</summary>
    private static (string First, string Second) Spellings(string root, string order)
    {
        var longForm = PathSpellings.Long(root);
        var shortForm = PathSpellings.Short(root);
        return order == "long-then-short" ? (longForm, shortForm) : (shortForm, longForm);
    }

    /// <summary>
    /// The second save is the one that matters: the file is <c>M</c> both times, so only the
    /// fingerprint tells the front-end its diff went stale — and only if the watcher reported the
    /// write and the warm path re-read the file rather than handing back what it held.
    /// </summary>
    [Test]
    public async Task A_modified_file_saved_again_after_the_index_was_warm_arrives_with_its_new_fingerprint()
    {
        using var copy = Committed(("readme.txt", "hello"));
        var firstSave = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var secondSave = firstSave.AddSeconds(1);

        await WithDaemon(
            copy,
            async client =>
            {
                await StatusAsync(client, copy.Root);
                copy.Write("readme.txt", "first edit");
                File.SetLastWriteTimeUtc(copy.Absolute("readme.txt"), firstSave);
                var first = await ListedAsync(
                    client,
                    copy.Root,
                    entry => entry.OnDisk == new FileFingerprint(10, firstSave)
                );

                copy.Write("readme.txt", "the second edit");
                File.SetLastWriteTimeUtc(copy.Absolute("readme.txt"), secondSave);
                var second = await ListedAsync(
                    client,
                    copy.Root,
                    entry => entry.OnDisk == new FileFingerprint(15, secondSave)
                );

                await Assert.That(first?.Status).IsEqualTo(NodeStatus.Modified);
                await Assert.That(second?.Status).IsEqualTo(NodeStatus.Modified);
                await Assert.That(second?.OnDisk).IsEqualTo(new FileFingerprint(15, secondSave));
            }
        );
    }

    /// <summary>Polls, like <see cref="NoticedAsync"/>, until readme.txt is listed as asked.</summary>
    /// <returns>The entry, or null if it never got there within the watcher's patience.</returns>
    private static async Task<WorkingCopyEntry?> ListedAsync(
        DaemonClient client,
        string root,
        Func<WorkingCopyEntry, bool> matches
    )
    {
        var deadline = DateTime.UtcNow + WatcherPatience;
        while (DateTime.UtcNow < deadline)
        {
            var status = await StatusAsync(client, root);
            var entry = status.Entries.SingleOrDefault(entry => entry.RelPath == "readme.txt");
            if (entry is not null && matches(entry))
            {
                return entry;
            }

            await Task.Delay(50);
        }

        return null;
    }

    private static async Task<StatusResponse> StatusAsync(DaemonClient client, string path)
    {
        var response = await client.SendAsync(
            new StatusRequest(path, IncludeUnmodified: false, IncludeIgnored: false),
            None
        );
        return response as StatusResponse
            ?? throw new InvalidOperationException($"Expected a status answer, got {response}.");
    }

    /// <summary>
    /// Filesystem events arrive when the platform feels like it, so this polls rather than sleeps
    /// for a fixed time — the contract being tested is "eventually, without a restart".
    /// </summary>
    private static async Task<bool> NoticedAsync(DaemonClient client, string root, string relPath)
    {
        var deadline = DateTime.UtcNow + WatcherPatience;
        while (DateTime.UtcNow < deadline)
        {
            var status = await StatusAsync(client, root);
            if (status.Entries.Any(entry => entry.RelPath == relPath))
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private static SvnWorkingCopy Committed(params (string RelPath, string Content)[] files)
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            foreach (var (relPath, content) in files)
            {
                copy.Write(relPath, content);
            }

            copy.Svn("add", "--quiet", "--force", ".");
            copy.Svn("commit", "--quiet", "-m", "fixture");

            // A commit leaves the root directory itself at the revision before it, and svn refuses
            // a later propset on an out-of-date directory.
            copy.Svn("update", "--quiet");
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A hosted service is started, not bound — nothing in its contract says the listener is up by
    /// the time <c>StartAsync</c> returns. Polling rather than sleeping keeps a loaded build
    /// machine from turning a passing test red, and checking the faulted task first means a server
    /// that could not bind reports why instead of timing out.
    /// </summary>
    private static async Task<DaemonClient> ConnectWhenListeningAsync(
        DaemonSocketServer server,
        string socketPath
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            if (server.ExecuteTask is { IsFaulted: true, Exception: { } failure })
            {
                throw failure;
            }

            try
            {
                return await DaemonClient.ConnectAsync(socketPath, None);
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }
    }

    private static async Task WithDaemon(SvnWorkingCopy copy, Func<DaemonClient, Task> use)
    {
        var socketPath = Path.Combine(Path.GetTempPath(), $"sv-{Guid.NewGuid():N}"[..12] + ".sock");

        // The real client, not a stand-in: the shell-out commands are the ones in the daemon whose
        // answer nobody can predict without running them.
        var svn = new SvnCommand("svn");
        var sessions = new WorkingCopySessions(
            new WorkingCopySessionFactory(
                GlobalIgnoreConfiguration.Load(),
                new SvnInfoCommand(svn).ReadAsync,
                new SvnStatusCommand(svn).ReadAsync,
                NullLogger<WorkingCopySessionFactory>.Instance
            ).OpenAsync
        );
        var server = new DaemonSocketServer(
            socketPath,
            new DaemonRequestHandler(
                sessions,
                new FakeDaemonShutdown(),
                TimeProvider.System,
                new SvnLogCommand(svn).ReadAsync,
                new SvnDiffCommand(svn).ReadAsync,
                new SvnAddCommand(svn).AddAsync,
                new SvnRevertCommand(svn).RevertAsync,
                new SvnDeleteCommand(svn).DeleteAsync,
                new NodeMove(
                    new SvnMoveCommand(svn),
                    new UnrecordedMoveRepair(new SvnMoveCommand(svn))
                ).MoveAsync,
                new SvnCommitCommand(svn).CommitAsync,
                new SvnUpdateCommand(svn).UpdateAsync,
                new SvnLockCommand(svn).LockAsync,
                new SvnLockCommand(svn).UnlockAsync,
                new SvnResolveCommand(svn).ResolveAsync,
                new SvnCleanupCommand(svn, PendingCleanup.Read).CleanUpAsync,
                LongPathSpelling.Of
            ),
            NullLogger<DaemonSocketServer>.Instance
        );

        await server.StartAsync(None);
        try
        {
            await using var client = await ConnectWhenListeningAsync(server, socketPath);
            await use(client);
        }
        finally
        {
            await server.StopAsync(None);
            sessions.Dispose();
            if (File.Exists(socketPath))
            {
                File.Delete(socketPath);
            }
        }
    }
}
