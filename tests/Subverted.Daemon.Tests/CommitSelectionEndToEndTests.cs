using Subverted.Core;
using Subverted.Protocol;
using Subverted.Svn.Tests;
using static Subverted.Daemon.Tests.DaemonEndToEndTests;

namespace Subverted.Daemon.Tests;

/// <summary>
/// Slice 3's commit over the whole chain: a real working copy, a real daemon, the real client and
/// a real repository. What reached the server is read back through the daemon's own log, because
/// "history kept" is a claim about the repository and not about the working copy.
/// </summary>
public sealed class CommitSelectionEndToEndTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task A_ticked_unversioned_file_is_added_and_committed()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Write("art/new.png", "fresh pixels");

        await WithDaemon(
            copy,
            async client =>
            {
                var committed = await CommitAsync(client, copy, "art/new.png");

                await Assert.That(committed.Revision).IsEqualTo(2L);
                await Assert.That(committed.Scheduled.Added).IsEquivalentTo(["art/new.png"]);
                await Assert.That((await StatusAsync(client, copy.Root)).Entries).IsEmpty();
                await Assert
                    .That(await ChangedInLatestAsync(client, copy))
                    .IsEquivalentTo([
                        new ChangedPath("/art/new.png", PathChange.Added, null, null),
                    ]);
            }
        );
    }

    /// <summary>
    /// A folder of new assets goes up whole, in the one revision — and a file the repository's
    /// ignore rules exclude stays local, because <c>svn add</c> skipped it and the commit names
    /// only what the add scheduled.
    /// </summary>
    [Test]
    public async Task A_ticked_unversioned_folder_commits_everything_in_it_except_what_is_ignored()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Svn("propset", "--quiet", "svn:global-ignores", "*.junk", copy.Root);
        copy.Svn("commit", "--quiet", "-m", "ignore junk", copy.Root);
        copy.Svn("update", "--quiet");
        copy.Write("level2/hero.png", "pixels");
        copy.Write("level2/props/crate.png", "wood");
        copy.Write("level2/props/build.junk", "build output");

        await WithDaemon(
            copy,
            async client =>
            {
                var committed = await CommitAsync(client, copy, "level2");

                await Assert.That(committed.Revision).IsEqualTo(3L);
                await Assert
                    .That(committed.Scheduled.Added)
                    .IsEquivalentTo([
                        "level2",
                        "level2/hero.png",
                        "level2/props",
                        "level2/props/crate.png",
                    ]);
                await Assert
                    .That((await ChangedInLatestAsync(client, copy)).Select(change => change.Path))
                    .IsEquivalentTo([
                        "/level2",
                        "/level2/hero.png",
                        "/level2/props",
                        "/level2/props/crate.png",
                    ]);

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
                    .That(
                        withIgnored
                            .Entries.Single(entry => entry.RelPath == "level2/props/build.junk")
                            .Status
                    )
                    .IsEqualTo(NodeStatus.Ignored);
            }
        );
    }

    [Test]
    public async Task A_ticked_missing_file_is_recorded_as_deleted_and_committed()
    {
        using var copy = Committed(("art/old.png", "pixels"), ("readme.txt", "hello"));
        copy.Delete("art/old.png");

        await WithDaemon(
            copy,
            async client =>
            {
                var committed = await CommitAsync(client, copy, "art/old.png");

                await Assert.That(committed.Revision).IsEqualTo(2L);
                await Assert.That(committed.Scheduled.Deleted).IsEquivalentTo(["art/old.png"]);
                await Assert
                    .That(await ChangedInLatestAsync(client, copy))
                    .IsEquivalentTo([
                        new ChangedPath("/art/old.png", PathChange.Deleted, null, null),
                    ]);
            }
        );
    }

    /// <summary>
    /// The whole reason the request exists. Committed as a plain delete and add, the file's history
    /// would stop at the old name; recorded as a move, the new path is a copy of the old one.
    /// </summary>
    [Test]
    public async Task A_ticked_hand_rename_is_committed_as_a_move_and_keeps_its_history()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        File.Move(copy.Absolute("art/hero.png"), copy.Absolute("art/protagonist.png"));

        await WithDaemon(
            copy,
            async client =>
            {
                var status = await StatusAsync(client, copy.Root);
                await Assert
                    .That(status.UnrecordedMoves)
                    .IsEquivalentTo([new UnrecordedMove("art/hero.png", "art/protagonist.png")]);

                var committed = await CommitAsync(
                    client,
                    copy,
                    "art/hero.png",
                    "art/protagonist.png"
                );

                await Assert
                    .That(committed.Scheduled.Moved)
                    .IsEquivalentTo([new RecordedMove("art/hero.png", "art/protagonist.png")]);
                await Assert.That(committed.Scheduled.Added).IsEmpty();
                await Assert.That(committed.Scheduled.Deleted).IsEmpty();
                await Assert
                    .That(await ChangedInLatestAsync(client, copy))
                    .IsEquivalentTo([
                        new ChangedPath("/art/hero.png", PathChange.Deleted, null, null),
                        new ChangedPath(
                            "/art/protagonist.png",
                            PathChange.Added,
                            "/art/hero.png",
                            1
                        ),
                    ]);
                await Assert
                    .That(File.ReadAllText(copy.Absolute("art/protagonist.png")))
                    .IsEqualTo("pixels");
            }
        );
    }

    /// <summary>
    /// One of each in one revision, with an unticked edit beside them that has to stay local — the
    /// commit is exactly the ticked set and not the working copy.
    /// </summary>
    [Test]
    public async Task A_mixed_selection_is_one_revision_holding_exactly_what_was_ticked()
    {
        using var copy = Committed(
            ("art/hero.png", "pixels"),
            ("art/old.png", "old pixels"),
            ("readme.txt", "hello"),
            ("notes.txt", "draft")
        );
        copy.Write("readme.txt", "hello, edited");
        copy.Write("notes.txt", "draft, edited but not ticked");
        copy.Write("art/new.png", "fresh pixels");
        copy.Delete("art/old.png");
        File.Move(copy.Absolute("art/hero.png"), copy.Absolute("art/protagonist.png"));

        await WithDaemon(
            copy,
            async client =>
            {
                var committed = await CommitAsync(
                    client,
                    copy,
                    "readme.txt",
                    "art/new.png",
                    "art/old.png",
                    "art/hero.png",
                    "art/protagonist.png"
                );

                await Assert.That(committed.Revision).IsEqualTo(2L);
                await Assert
                    .That(await ChangedInLatestAsync(client, copy))
                    .IsEquivalentTo([
                        new ChangedPath("/readme.txt", PathChange.Modified, null, null),
                        new ChangedPath("/art/new.png", PathChange.Added, null, null),
                        new ChangedPath("/art/old.png", PathChange.Deleted, null, null),
                        new ChangedPath("/art/hero.png", PathChange.Deleted, null, null),
                        new ChangedPath(
                            "/art/protagonist.png",
                            PathChange.Added,
                            "/art/hero.png",
                            1
                        ),
                    ]);

                var after = await StatusAsync(client, copy.Root);
                await Assert
                    .That(after.Entries.Select(entry => (entry.RelPath, entry.Status)))
                    .IsEquivalentTo([("notes.txt", NodeStatus.Modified)]);
            }
        );
    }

    /// <summary>
    /// The server refuses after every mark was made. Nothing is rolled back: the working copy reads
    /// <c>A</c> and <c>D</c>, SVN's own words come back, and the same request then just works.
    /// </summary>
    [Test]
    public async Task A_commit_refused_by_the_server_leaves_the_schedule_and_a_retry_commits_it()
    {
        using var copy = Committed(("art/old.png", "pixels"), ("readme.txt", "hello"));
        copy.Write("art/new.png", "fresh pixels");
        copy.Delete("art/old.png");
        var hook = RefuseEveryCommit(copy);

        await WithDaemon(
            copy,
            async client =>
            {
                var request = Selection(copy, "art/new.png", "art/old.png");
                var response = await client.SendAsync(request, None);

                await Assert.That(response).IsTypeOf<SelectionNotCommittedResponse>();
                var stopped = (SelectionNotCommittedResponse)response;
                await Assert.That(stopped.FailedStep).IsEqualTo(SelectionStep.Commit);
                await Assert.That(stopped.Failure).Contains("the studio hook says no");
                await Assert.That(stopped.Scheduled.Added).IsEquivalentTo(["art/new.png"]);
                await Assert.That(stopped.Scheduled.Deleted).IsEquivalentTo(["art/old.png"]);

                var left = await StatusAsync(client, copy.Root);
                await Assert
                    .That(left.Entries.Select(entry => (entry.RelPath, entry.Status)))
                    .IsEquivalentTo([
                        ("art/new.png", NodeStatus.Added),
                        ("art/old.png", NodeStatus.Deleted),
                    ]);

                File.Delete(hook);
                var retried = await client.SendAsync(request, None);

                await Assert.That(retried).IsTypeOf<CommitSelectionResponse>();
                await Assert.That(((CommitSelectionResponse)retried).Revision).IsEqualTo(2L);
                await Assert.That((await StatusAsync(client, copy.Root)).Entries).IsEmpty();
            }
        );
    }

    [Test]
    public async Task One_half_of_a_hand_rename_is_refused_and_nothing_is_touched()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        File.Move(copy.Absolute("art/hero.png"), copy.Absolute("art/protagonist.png"));

        await WithDaemon(
            copy,
            async client =>
            {
                await StatusAsync(client, copy.Root);
                var response = await client.SendAsync(Selection(copy, "art/hero.png"), None);

                await Assert.That(response).IsTypeOf<ErrorResponse>();
                await Assert
                    .That(((ErrorResponse)response).Kind)
                    .IsEqualTo(DaemonErrorKind.RequestRefused);

                var status = await StatusAsync(client, copy.Root);
                await Assert
                    .That(status.Entries.Select(entry => (entry.RelPath, entry.Status)))
                    .IsEquivalentTo([
                        ("art/hero.png", NodeStatus.Missing),
                        ("art/protagonist.png", NodeStatus.Unversioned),
                    ]);
            }
        );
    }

    private static CommitSelectionRequest Selection(SvnWorkingCopy copy, params string[] ticked) =>
        new([.. ticked.Select(copy.Absolute)], "from the commit composer");

    private static async Task<CommitSelectionResponse> CommitAsync(
        DaemonClient client,
        SvnWorkingCopy copy,
        params string[] ticked
    )
    {
        var response = await client.SendAsync(Selection(copy, ticked), None);
        return response as CommitSelectionResponse
            ?? throw new InvalidOperationException($"Expected a commit, got {response}.");
    }

    /// <summary>
    /// The paths the newest revision changed, as the repository recorded them. The update first,
    /// because a commit leaves the root at the revision before it and its log would stop there.
    /// </summary>
    private static async Task<IReadOnlyList<ChangedPath>> ChangedInLatestAsync(
        DaemonClient client,
        SvnWorkingCopy copy
    )
    {
        copy.Svn("update", "--quiet");
        var log = (LogResponse)await client.SendAsync(new LogRequest(copy.Root, Limit: 1), None);
        return log.Revisions[0].ChangedPaths;
    }

    /// <summary>A pre-commit hook that says no, the way a studio's server might.</summary>
    /// <returns>The hook's path, so a test can take it away again.</returns>
    private static string RefuseEveryCommit(SvnWorkingCopy copy)
    {
        var hooks = Path.Combine(copy.RepositoryPath, "hooks");
        if (OperatingSystem.IsWindows())
        {
            var batch = Path.Combine(hooks, "pre-commit.bat");
            File.WriteAllText(
                batch,
                "@echo off\r\necho the studio hook says no 1>&2\r\nexit 1\r\n"
            );
            return batch;
        }

        var script = Path.Combine(hooks, "pre-commit");
        File.WriteAllText(script, "#!/bin/sh\necho the studio hook says no >&2\nexit 1\n");
        File.SetUnixFileMode(
            script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
        return script;
    }
}
