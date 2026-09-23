using Microsoft.Extensions.Logging.Abstractions;
using Subverted.Core;
using Subverted.Protocol;
using Subverted.Svn;
using Subverted.Svn.Tests;

namespace Subverted.Daemon.Tests;

/// <summary>
/// Which of the two readers answers for a working copy. The client's own behaviour is covered in
/// <c>SvnFallbackIntegrationTests</c>; what is decided here is when it gets asked at all — and the
/// answer has to be "only when wc.db could not", because the fallback costs a process per scan.
/// </summary>
/// <remarks>
/// The client is faked rather than run. A wc.db written by a newer Subversion is the case the
/// fallback exists for, and the client installed here could not read one either — so the only part
/// of it a test on this machine can pin is the decision.
/// </remarks>
public sealed class WorkingCopySessionFactoryTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task A_working_copy_whose_database_cannot_be_read_is_served_by_the_svn_client()
    {
        using var copy = Committed();
        Corrupt(copy);
        var client = new FakeClient(copy.Root, [Entry("art/hero.png", NodeStatus.Modified)]);

        using var session = await Factory(client).OpenAsync(copy.Root, None);
        var current = await session.CurrentAsync(None);

        await Assert.That(client.Scans).IsEqualTo(1);
        await Assert
            .That(current.Entries.Select(entry => entry.RelPath))
            .IsEquivalentTo(["art/hero.png"]);
    }

    /// <summary>
    /// A null format is the fallback's signature, and the only thing that tells a caller which
    /// reader answered. Reporting a made-up number would claim the version gate had passed.
    /// </summary>
    [Test]
    public async Task A_session_the_client_serves_reports_no_schema_format()
    {
        using var copy = Committed();
        Corrupt(copy);

        using var session = await Factory(new FakeClient(copy.Root, [])).OpenAsync(copy.Root, None);

        await Assert.That(session.Info.Format).IsNull();
    }

    [Test]
    public async Task A_working_copy_wc_db_can_read_is_never_put_through_the_client()
    {
        using var copy = Committed();
        var client = new FakeClient(copy.Root, []);

        using var session = await Factory(client).OpenAsync(copy.Root, None);
        await session.CurrentAsync(None);

        await Assert.That(client.Reads).IsEqualTo(0);
        await Assert.That(client.Scans).IsEqualTo(0);
        await Assert.That(session.Info.Format).IsNotNull();
    }

    /// <summary>
    /// The client can name a root the daemon cannot watch — a network share, or Linux out of inotify
    /// watches. That is a session that rescans every time, not a session that fails to open.
    /// </summary>
    [Test]
    public async Task A_root_that_cannot_be_watched_still_opens_and_says_it_is_not_watching()
    {
        using var copy = Committed();
        Corrupt(copy);
        var unwatchable = Path.Combine(copy.Root, "no-such-directory");

        using var session = await Factory(new FakeClient(unwatchable, []))
            .OpenAsync(copy.Root, None);

        await Assert.That(session.WatcherState).IsEqualTo(WatcherState.Unavailable);
    }

    /// <summary>
    /// "There is no working copy here" is the caller's path being wrong, not the fast path failing.
    /// Asking the client about it would turn a clear answer into a slower, vaguer one.
    /// </summary>
    [Test]
    public async Task A_path_in_no_working_copy_fails_rather_than_being_asked_of_the_client()
    {
        var directory = Directory.CreateTempSubdirectory("subverted-notawc-");
        var client = new FakeClient(directory.FullName, []);
        try
        {
            await Assert
                .That(async () => await Factory(client).OpenAsync(directory.FullName, None))
                .Throws<WcDbException>();
            await Assert.That(client.Reads).IsEqualTo(0);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Both readers failing is a real state — a wc.db Subversion cannot read either — and it has to
    /// arrive as a failure. An empty listing would read as a clean working copy.
    /// </summary>
    [Test]
    public async Task A_client_that_cannot_answer_either_fails_rather_than_serving_nothing()
    {
        using var copy = Committed();
        Corrupt(copy);

        var factory = new WorkingCopySessionFactory(
            [],
            (_, _) => throw new SvnCommandException("svn: E155016: the database is corrupt."),
            (_, _) => throw new SvnCommandException("unreachable"),
            NullLogger<WorkingCopySessionFactory>.Instance
        );

        await Assert
            .That(async () => await factory.OpenAsync(copy.Root, None))
            .Throws<SvnCommandException>()
            .WithMessageContaining("E155016");
    }

    private static WorkingCopySessionFactory Factory(FakeClient client) =>
        new(
            [],
            client.ReadInfoAsync,
            client.ReadStatusAsync,
            NullLogger<WorkingCopySessionFactory>.Instance
        );

    /// <summary>Leaves wc.db where it is and unreadable, which is what sends the factory to the client.</summary>
    private static void Corrupt(SvnWorkingCopy copy) =>
        copy.Write(".svn/wc.db", "this is not a database");

    private static SvnWorkingCopy Committed()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("art/hero.png", "pixels");
            copy.Svn("add", "--quiet", "art");
            copy.Svn("commit", "--quiet", "-m", "fixture");
            copy.Svn("update", "--quiet");
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    private static WorkingCopyEntry Entry(string relPath, NodeStatus status) =>
        new(
            relPath,
            NodeKind.Unknown,
            status,
            PropertyStatus.Unmodified,
            1,
            null,
            false,
            false,
            false,
            false
        );

    /// <summary>
    /// Stands in for the two <c>svn</c> reads, counting both so "was the client asked" is an
    /// assertion rather than an inference from what came back.
    /// </summary>
    private sealed class FakeClient(string rootPath, IReadOnlyList<WorkingCopyEntry> entries)
    {
        public int Reads { get; private set; }

        public int Scans { get; private set; }

        public Task<WorkingCopyInfo> ReadInfoAsync(string path, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(
                new WorkingCopyInfo(rootPath, "https://svn.example/repo", "uuid-1", null)
            );
        }

        public Task<IReadOnlyList<WorkingCopyEntry>> ReadStatusAsync(
            string workingCopyRoot,
            CancellationToken cancellationToken
        )
        {
            Scans++;
            return Task.FromResult(entries);
        }
    }
}
