using Subverted.Core;
using Subverted.Daemon;
using Subverted.Protocol;

namespace Subverted.Daemon.Tests;

/// <summary>
/// Respelled with a marker, so a path that was respelled is visibly different from one that was
/// not. Every case also checks the fields that are not paths came through untouched.
/// </summary>
public sealed class RequestSpellingTests
{
    private static string Mark(string path) => $"long:{path}";

    /// <summary>A request type added to Protocol and not here would reach the daemon unrespelled.</summary>
    [Test]
    public async Task Every_request_type_is_covered()
    {
        var all = typeof(DaemonRequest)
            .Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(DaemonRequest)) && !type.IsAbstract)
            .ToHashSet();

        await Assert.That(all.SetEquals(RequestSpelling.Covered)).IsTrue();
    }

    [Test]
    public async Task A_status_request_respells_its_path_and_its_scope()
    {
        var respelled = (StatusRequest)
            RequestSpelling.Respell(new StatusRequest("/wc", true, false, Scope: ["/wc/a"]), Mark);

        await Assert.That(respelled.WorkingCopyPath).IsEqualTo("long:/wc");
        await Assert.That(respelled.Scope).IsEquivalentTo(new[] { "long:/wc/a" });
        await Assert.That(respelled.IncludeUnmodified).IsTrue();
        await Assert.That(respelled.IncludeIgnored).IsFalse();
    }

    /// <summary>No scope means the whole copy; respelling must not turn it into an empty one.</summary>
    [Test]
    public async Task A_status_request_without_a_scope_keeps_none()
    {
        var respelled = (StatusRequest)
            RequestSpelling.Respell(new StatusRequest("/wc", false, false), Mark);

        await Assert.That(respelled.Scope).IsNull();
    }

    [Test]
    public async Task A_move_respells_both_ends()
    {
        var respelled = (MoveRequest)
            RequestSpelling.Respell(new MoveRequest("/wc/a", "/wc/b"), Mark);

        await Assert.That(respelled).IsEqualTo(new MoveRequest("long:/wc/a", "long:/wc/b"));
    }

    [Test]
    public async Task A_commit_respells_its_paths_and_keeps_its_message_and_scope()
    {
        var respelled = (CommitRequest)
            RequestSpelling.Respell(
                new CommitRequest(["/wc/a", "/wc/b"], "a message", CommitScope.ExactlyTheseNodes),
                Mark
            );

        await Assert.That(respelled.Paths).IsEquivalentTo(new[] { "long:/wc/a", "long:/wc/b" });
        await Assert.That(respelled.Message).IsEqualTo("a message");
        await Assert.That(respelled.Scope).IsEqualTo(CommitScope.ExactlyTheseNodes);
    }

    [Test]
    public async Task A_lock_respells_its_paths_and_keeps_its_comment_and_foreign_rule()
    {
        var respelled = (LockRequest)
            RequestSpelling.Respell(
                new LockRequest(["/wc/a"], "painting", ForeignLock.Overridden),
                Mark
            );

        await Assert.That(respelled.Paths).IsEquivalentTo(new[] { "long:/wc/a" });
        await Assert.That(respelled.Comment).IsEqualTo("painting");
        await Assert.That(respelled.Foreign).IsEqualTo(ForeignLock.Overridden);
    }

    [Test]
    public async Task An_unlock_respells_its_paths_and_keeps_its_foreign_rule()
    {
        var respelled = (UnlockRequest)
            RequestSpelling.Respell(new UnlockRequest(["/wc/a"], ForeignLock.Overridden), Mark);

        await Assert.That(respelled.Paths).IsEquivalentTo(new[] { "long:/wc/a" });
        await Assert.That(respelled.Foreign).IsEqualTo(ForeignLock.Overridden);
    }

    [Test]
    public async Task A_resolve_respells_its_paths_and_keeps_its_resolution()
    {
        var respelled = (ResolveRequest)
            RequestSpelling.Respell(new ResolveRequest(["/wc/a"], ConflictResolution.Mine), Mark);

        await Assert.That(respelled.Paths).IsEquivalentTo(new[] { "long:/wc/a" });
        await Assert.That(respelled.Resolution).IsEqualTo(ConflictResolution.Mine);
    }

    [Test]
    public async Task A_log_respells_its_path_and_keeps_its_limit()
    {
        var respelled = (LogRequest)RequestSpelling.Respell(new LogRequest("/wc", 20), Mark);

        await Assert.That(respelled).IsEqualTo(new LogRequest("long:/wc", 20));
    }

    [Test]
    public async Task A_log_keeps_where_it_starts()
    {
        var respelled = RequestSpelling.Respell(
            new LogRequest("/wc", 20, new HistoryFromRevision(41)),
            Mark
        );

        await Assert
            .That(respelled)
            .IsEqualTo(new LogRequest("long:/wc", 20, new HistoryFromRevision(41)));
    }

    /// <summary>The repository path is not on disk, so there is nothing to respell it against.</summary>
    [Test]
    public async Task A_revision_diff_respells_its_working_copy_path_and_not_its_repository_path()
    {
        var respelled = RequestSpelling.Respell(
            new RevisionDiffRequest("/wc", "/trunk/a.txt", 7),
            Mark
        );

        await Assert
            .That(respelled)
            .IsEqualTo(new RevisionDiffRequest("long:/wc", "/trunk/a.txt", 7));
    }

    [Test]
    public async Task A_working_copy_revision_request_respells_its_path()
    {
        var respelled = RequestSpelling.Respell(new WorkingCopyRevisionRequest("/wc/art"), Mark);

        await Assert.That(respelled).IsEqualTo(new WorkingCopyRevisionRequest("long:/wc/art"));
    }

    [Test]
    public async Task Single_path_requests_respell_their_path()
    {
        await Assert
            .That(((DiffRequest)RequestSpelling.Respell(new DiffRequest("/wc"), Mark)).Path)
            .IsEqualTo("long:/wc");
        await Assert
            .That(((UpdateRequest)RequestSpelling.Respell(new UpdateRequest("/wc"), Mark)).Path)
            .IsEqualTo("long:/wc");
        await Assert
            .That(((CleanupRequest)RequestSpelling.Respell(new CleanupRequest("/wc"), Mark)).Path)
            .IsEqualTo("long:/wc");
    }

    [Test]
    public async Task Multi_path_requests_respell_every_path()
    {
        string[] expected = ["long:/wc/a", "long:/wc/b"];
        IReadOnlyList<string> paths = ["/wc/a", "/wc/b"];

        await Assert
            .That(((AddRequest)RequestSpelling.Respell(new AddRequest(paths), Mark)).Paths)
            .IsEquivalentTo(expected);
        await Assert
            .That(((RevertRequest)RequestSpelling.Respell(new RevertRequest(paths), Mark)).Paths)
            .IsEquivalentTo(expected);
        await Assert
            .That(((DeleteRequest)RequestSpelling.Respell(new DeleteRequest(paths), Mark)).Paths)
            .IsEquivalentTo(expected);
    }

    [Test]
    public async Task A_request_with_no_paths_passes_through_as_itself()
    {
        var info = new DaemonInfoRequest();
        var shutdown = new ShutdownRequest();

        await Assert.That(RequestSpelling.Respell(info, Mark)).IsSameReferenceAs(info);
        await Assert.That(RequestSpelling.Respell(shutdown, Mark)).IsSameReferenceAs(shutdown);
    }
}
