using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Subverted.Daemon;
using Subverted.Protocol;
using Subverted.Svn;

// Before anything can start svn: every child inherits this console, and its code page is the
// encoding svn writes paths in (D33).
using var svnConsole = SvnConsole.UseUtf8();

var socketPath = DaemonSocketPath.FromEnvironment();
var builder = Host.CreateApplicationBuilder(args);

// Started by `sv`, the daemon would otherwise write its log across whatever the user is reading.
// The file next to the socket is where it goes instead. Console is nulled as well as unhooked:
// `sv` closes the pipes when it exits, and a stray write to a dead pipe would take the daemon
// down long after anyone could connect the two events.
if (args.Contains("--detached"))
{
    builder.Logging.ClearProviders();
    Console.SetOut(TextWriter.Null);
    Console.SetError(TextWriter.Null);
}

builder.Logging.AddProvider(new FileLoggerProvider(Path.ChangeExtension(socketPath, ".log")));

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton(new SvnCommand("svn"));

// Read once: the runtime config's ignore patterns are the same for every working copy, and
// re-reading them per scan would be I/O for an answer that cannot have changed. The two svn reads
// beside them are D1's fallback, for a wc.db this build cannot understand.
builder.Services.AddSingleton(provider => new WorkingCopySessionFactory(
    GlobalIgnoreConfiguration.Load(),
    new SvnInfoCommand(provider.GetRequiredService<SvnCommand>()).ReadAsync,
    new SvnStatusCommand(provider.GetRequiredService<SvnCommand>()).ReadAsync,
    provider.GetRequiredService<ILogger<WorkingCopySessionFactory>>()
));
builder.Services.AddSingleton(provider => new WorkingCopySessions(
    provider.GetRequiredService<WorkingCopySessionFactory>().OpenAsync
));
builder.Services.AddSingleton<IDaemonShutdown, HostDaemonShutdown>();

// Log and diff are server round trips, so they shell out (D6). The handler takes the two reads as
// delegates rather than as SVN types, which is what keeps it testable without `svn` on PATH.
builder.Services.AddSingleton<ReadRevisionLog>(provider =>
    new SvnLogCommand(provider.GetRequiredService<SvnCommand>()).ReadAsync
);
builder.Services.AddSingleton<ReadWorkingCopyDiff>(provider =>
    new SvnDiffCommand(provider.GetRequiredService<SvnCommand>()).ReadAsync
);
builder.Services.AddSingleton<ReadRevisionDiff>(provider =>
    new SvnRevisionDiffCommand(provider.GetRequiredService<SvnCommand>()).ReadAsync
);

// svn diff prints three lines of context and no more, so a wider diff is written here from the
// pristine or two `svn cat`s — only where that is provably svn's own diff, and svn's otherwise.
builder.Services.AddSingleton<ReadWorkingCopyContextDiff>(provider =>
    new WorkingCopyContextDiff(
        provider.GetRequiredService<SvnCommand>().Spelling,
        Environment.NewLine
    ).ReadAsync
);
builder.Services.AddSingleton<ReadRevisionContextDiff>(provider =>
    new RevisionContextDiff(provider.GetRequiredService<SvnCommand>(), Environment.NewLine).ReadAsync
);

// Local, but a read of wc.db like status: svnversion is the fallback for a schema this build
// does not understand, and it ships beside svn.
builder.Services.AddSingleton<ReadBaseRevisionRange>(
    new BaseRevisionRangeReader(new SvnVersionCommand(new SvnCommand("svnversion"))).ReadAsync
);

// The eight that change a working copy. They go through the client for the same reason log and diff
// do — it is the only implementation of SVN's semantics anyone has agreed on — and the daemon drops
// its held index afterwards rather than waiting for the watcher to tell it what it already knows.
builder.Services.AddSingleton<ScheduleAddition>(provider =>
    new SvnAddCommand(provider.GetRequiredService<SvnCommand>()).AddAsync
);
builder.Services.AddSingleton<RevertChanges>(provider =>
    new SvnRevertCommand(provider.GetRequiredService<SvnCommand>()).RevertAsync
);
builder.Services.AddSingleton<ScheduleDeletion>(provider =>
    new SvnDeleteCommand(provider.GetRequiredService<SvnCommand>()).DeleteAsync
);

// The rename is the one that is not a single client call: `svn move` needs its source on disk, and
// after a rename made in a file manager it is not there — so the repair walks the working copy back
// to a state SVN can record from. Which of the two runs is decided from the filesystem, not asked.
builder.Services.AddSingleton<RenameNode>(provider =>
{
    var svn = provider.GetRequiredService<SvnCommand>();
    var move = new SvnMoveCommand(svn);
    return new NodeMove(move, new UnrecordedMoveRepair(move)).MoveAsync;
});

builder.Services.AddSingleton<CommitChanges>(provider =>
    new SvnCommitCommand(provider.GetRequiredService<SvnCommand>()).CommitAsync
);
builder.Services.AddSingleton<BringUpToDate>(provider =>
    new SvnUpdateCommand(provider.GetRequiredService<SvnCommand>()).UpdateAsync
);
builder.Services.AddSingleton<AcquireLocks>(provider =>
    new SvnLockCommand(provider.GetRequiredService<SvnCommand>()).LockAsync
);
builder.Services.AddSingleton<ReleaseLocks>(provider =>
    new SvnLockCommand(provider.GetRequiredService<SvnCommand>()).UnlockAsync
);
builder.Services.AddSingleton<ResolveConflicts>(provider =>
    new SvnResolveCommand(provider.GetRequiredService<SvnCommand>()).ResolveAsync
);

// Cleanup is the odd one: the client prints nothing whatever it did, so the command reads wc.db on
// both sides of the run and the difference is the whole report.
builder.Services.AddSingleton<CleanUpWorkingCopy>(provider =>
    new SvnCleanupCommand(
        provider.GetRequiredService<SvnCommand>(),
        PendingCleanup.Read
    ).CleanUpAsync
);
builder.Services.AddSingleton<RecordDeletion>(provider =>
    new SvnRecordDeletionCommand(provider.GetRequiredService<SvnCommand>()).RecordAsync
);
builder.Services.AddSingleton<SelectionCommitter>();
builder.Services.AddSingleton<RespellPath>(LongPathSpelling.Of);
builder.Services.AddSingleton<DaemonRequestHandler>();

builder.Services.AddHostedService<IndexWarmer>();
builder.Services.AddHostedService(provider => new DaemonSocketServer(
    socketPath,
    provider.GetRequiredService<DaemonRequestHandler>(),
    provider.GetRequiredService<ILogger<DaemonSocketServer>>()
));

await builder.Build().RunAsync();
