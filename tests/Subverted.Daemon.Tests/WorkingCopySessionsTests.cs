using Subverted.Svn;

namespace Subverted.Daemon.Tests;

/// <summary>
/// Opening a working copy costs a SQLite handle and a filesystem watch, so "have I already got
/// this one" has to be right — both for the obvious nested path and for the case where the root
/// the SVN layer reports is not one the request path sits under.
/// </summary>
public sealed class WorkingCopySessionsTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task A_second_path_in_the_same_working_copy_reuses_the_open_session()
    {
        var opens = 0;
        using var sessions = new WorkingCopySessions(
            (_, token) =>
            {
                opens++;
                return NewSession("/wc", token);
            }
        );

        var first = await sessions.ForAsync(FullPath("/wc"), None);
        var second = await sessions.ForAsync(FullPath("/wc/art/hero.png"), None);

        await Assert.That(second).IsSameReferenceAs(first);
        await Assert.That(opens).IsEqualTo(1);
    }

    [Test]
    public async Task Two_working_copies_get_two_sessions()
    {
        using var sessions = new WorkingCopySessions(NewSession);

        var first = await sessions.ForAsync(FullPath("/wc"), None);
        var second = await sessions.ForAsync(FullPath("/other"), None);

        await Assert.That(second).IsNotSameReferenceAs(first);
        await Assert.That(sessions.All.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Nothing_is_held_before_anything_is_asked_for()
    {
        using var sessions = new WorkingCopySessions(NewSession);

        await Assert.That(sessions.All).IsEmpty();
    }

    /// <summary>
    /// The lookup can only miss the way it does here: the SVN layer reports a root the request path
    /// does not sit under. The duplicate is closed rather than kept, so a second wc.db handle never
    /// outlives the request that opened it.
    /// </summary>
    [Test]
    public async Task A_root_that_is_already_held_closes_the_session_that_just_opened_it()
    {
        var opened = new List<FakeWorkingCopyScan>();
        using var sessions = new WorkingCopySessions(
            (_, _) =>
            {
                var scan = new FakeWorkingCopyScan("/wc");
                opened.Add(scan);
                return Task.FromResult(new WorkingCopySession(scan, new FakeChangeNotifier()));
            }
        );

        var first = await sessions.ForAsync(FullPath("/elsewhere"), None);
        var second = await sessions.ForAsync(FullPath("/somewhere-else"), None);

        await Assert.That(second).IsSameReferenceAs(first);
        await Assert.That(opened.Count).IsEqualTo(2);
        await Assert.That(opened[1].IsDisposed).IsTrue();
        await Assert.That(sessions.All.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_path_in_no_working_copy_fails_the_way_the_svn_layer_says_it_does()
    {
        using var sessions = new WorkingCopySessions(
            (path, _) =>
                throw new WcDbException(WcDbFailure.NotAWorkingCopy, $"nothing at '{path}'.")
        );

        await Assert
            .That(async () => await sessions.ForAsync(FullPath("/elsewhere"), None))
            .Throws<WcDbException>();
    }

    [Test]
    public async Task A_failed_open_is_not_remembered_as_a_working_copy()
    {
        var attempts = 0;
        using var sessions = new WorkingCopySessions(
            (path, _) =>
            {
                attempts++;
                throw new WcDbException(WcDbFailure.NotAWorkingCopy, $"nothing at '{path}'.");
            }
        );

        for (var i = 0; i < 2; i++)
        {
            try
            {
                await sessions.ForAsync(FullPath("/elsewhere"), None);
            }
            catch (WcDbException) { }
        }

        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(sessions.All).IsEmpty();
    }

    [Test]
    public async Task Disposing_closes_every_working_copy_it_holds()
    {
        var scans = new List<FakeWorkingCopyScan>();
        var sessions = new WorkingCopySessions(
            (path, _) =>
            {
                var scan = new FakeWorkingCopyScan(path);
                scans.Add(scan);
                return Task.FromResult(new WorkingCopySession(scan, new FakeChangeNotifier()));
            }
        );
        await sessions.ForAsync(FullPath("/wc"), None);
        await sessions.ForAsync(FullPath("/other"), None);

        sessions.Dispose();

        await Assert.That(scans.Count).IsEqualTo(2);
        await Assert.That(scans.All(scan => scan.IsDisposed)).IsTrue();
        await Assert.That(sessions.All).IsEmpty();
    }

    private static Task<WorkingCopySession> NewSession(
        string rootPath,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new WorkingCopySession(new FakeWorkingCopyScan(rootPath), new FakeChangeNotifier())
        );

    private static string FullPath(string path) => Path.GetFullPath(path);
}
