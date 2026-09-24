using System.Reflection;
using Subverted.Core;
using Subverted.Protocol;
using Subverted.Svn;

namespace Subverted.Daemon;

/// <summary>
/// Answers one request. Everything slow is behind <see cref="WorkingCopySessions"/>; what happens
/// here is dispatch, filtering and turning an SVN failure into something a front-end can print.
/// </summary>
/// <param name="clock">
/// Read once at construction for the uptime baseline, so "since the daemon started" means since
/// this object was built — which is startup.
/// </param>
public sealed class DaemonRequestHandler(
    WorkingCopySessions sessions,
    IDaemonShutdown shutdown,
    TimeProvider clock,
    ReadRevisionLog readRevisionLog,
    ReadWorkingCopyDiff readWorkingCopyDiff,
    ReadRevisionDiff readRevisionDiff,
    ReadWorkingCopyContextDiff readWorkingCopyContextDiff,
    ReadRevisionContextDiff readRevisionContextDiff,
    ReadBaseRevisionRange readBaseRevisionRange,
    ScheduleAddition scheduleAddition,
    RevertChanges revertChanges,
    ScheduleDeletion scheduleDeletion,
    RenameNode renameNode,
    CommitChanges commitChanges,
    BringUpToDate bringUpToDate,
    AcquireLocks acquireLocks,
    ReleaseLocks releaseLocks,
    ResolveConflicts resolveConflicts,
    CleanUpWorkingCopy cleanUpWorkingCopy,
    RespellPath respellPath,
    SelectionCommitter selectionCommitter
)
{
    private static readonly string Version =
        typeof(DaemonRequestHandler)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0";

    private readonly DateTimeOffset _startedAt = clock.GetUtcNow();

    /// <summary>
    /// Answers one request, with every path in it respelled first: everything below compares paths
    /// as strings, and one folder has two spellings on Windows.
    /// </summary>
    public Task<DaemonResponse> HandleAsync(
        DaemonRequest request,
        CancellationToken cancellationToken
    ) =>
        DispatchAsync(
            RequestSpelling.Respell(request, path => respellPath(path)),
            cancellationToken
        );

    private async Task<DaemonResponse> DispatchAsync(
        DaemonRequest request,
        CancellationToken cancellationToken
    ) =>
        request switch
        {
            StatusRequest status => await StatusAsync(status, cancellationToken),
            LogRequest log => await LogAsync(log, cancellationToken),
            DiffRequest diff => await DiffAsync(diff, cancellationToken),
            RevisionDiffRequest revisionDiff => await RevisionDiffAsync(
                revisionDiff,
                cancellationToken
            ),
            WorkingCopyRevisionRequest revision => await WorkingCopyRevisionAsync(
                revision,
                cancellationToken
            ),
            AddRequest add => await AddAsync(add, cancellationToken),
            RevertRequest revert => await RevertAsync(revert, cancellationToken),
            DeleteRequest delete => await DeleteAsync(delete, cancellationToken),
            MoveRequest move => await MoveAsync(move, cancellationToken),
            CommitRequest commit => await CommitAsync(commit, cancellationToken),
            CommitSelectionRequest selection => await CommitSelectionAsync(
                selection,
                cancellationToken
            ),
            UpdateRequest update => await UpdateAsync(update, cancellationToken),
            LockRequest take => await LockAsync(take, cancellationToken),
            UnlockRequest release => await UnlockAsync(release, cancellationToken),
            ResolveRequest resolve => await ResolveAsync(resolve, cancellationToken),
            CleanupRequest cleanup => await CleanupAsync(cleanup, cancellationToken),
            DaemonInfoRequest => DaemonInfo(),
            ShutdownRequest => Shutdown(),
            _ => new ErrorResponse(
                DaemonErrorKind.Internal,
                $"This daemon has no handler for {request.GetType().Name}."
            ),
        };

    private async Task<DaemonResponse> StatusAsync(
        StatusRequest request,
        CancellationToken cancellationToken
    )
    {
        var startedAt = clock.GetTimestamp();
        try
        {
            var session = await sessions.ForAsync(request.WorkingCopyPath, cancellationToken);
            var comparison = ContainingRoot.PlatformComparison;
            if (
                request.Scope is { } targets
                && WorkingCopyTargets.FirstOutside(session.Info.RootPath, targets, comparison)
                    is { } outside
            )
            {
                return new ErrorResponse(
                    DaemonErrorKind.NotAWorkingCopy,
                    $"'{outside}' is not in the working copy at '{session.Info.RootPath}'. "
                        + "One status covers one working copy."
                );
            }

            var scope = request
                .Scope?.Select(target => TargetCoverage.RelativeTo(session.Info.RootPath, target))
                .ToList();
            var current = await session.CurrentAsync(cancellationToken);
            if (request.HeldScan == current.ScanId)
            {
                return new StatusUnchangedResponse(
                    current.ScanId,
                    clock.GetElapsedTime(startedAt).TotalMilliseconds
                );
            }

            return new StatusResponse(
                session.Info,
                StatusFilter.Apply(
                    StatusFilter.Within(current.Entries, scope, comparison),
                    request.IncludeUnmodified,
                    request.IncludeIgnored
                ),
                current.ServedFromWarmIndex,
                clock.GetElapsedTime(startedAt).TotalMilliseconds,
                current.UnfinishedOperations,
                StatusFilter.MovesWithin(current.UnrecordedMoves, scope, comparison),
                current.ScanId
            );
        }
        catch (WcDbException ex)
        {
            return new ErrorResponse(Translate(ex.Failure), ex.Message);
        }
        catch (SvnCommandException ex)
        {
            // Only reachable once wc.db has already failed: status is served from the fast path
            // unless the session fell back to the client, and then the client's failure is the
            // whole answer.
            return new ErrorResponse(DaemonErrorKind.SvnCommandFailed, ex.Message);
        }
    }

    /// <remarks>
    /// A start below revision 1 is refused rather than passed on: <c>-r 0:1</c> is a range SVN
    /// accepts, and it lists revision 1 as though it were the page asked for.
    /// </remarks>
    private async Task<DaemonResponse> LogAsync(
        LogRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.Start is HistoryFromRevision { Revision: < 1 } from)
        {
            return new ErrorResponse(
                DaemonErrorKind.RequestRefused,
                $"History cannot start at revision {from.Revision}; revisions start at 1."
            );
        }

        return await ShellingOutAsync(
            request.Path,
            async session => new LogResponse(
                await readRevisionLog(
                    session.Info.RootPath,
                    request.Path,
                    request.Limit,
                    request.Start,
                    cancellationToken
                )
            ),
            cancellationToken
        );
    }

    /// <remarks>
    /// <c>svn diff</c> answers unless more context was asked for, and whenever the in-process diff
    /// declines — so a request without a context is exactly what it was before contexts existed.
    /// </remarks>
    private async Task<DaemonResponse> DiffAsync(
        DiffRequest request,
        CancellationToken cancellationToken
    )
    {
        if (RefusedContext(request.Context) is { } refusal)
        {
            return refusal;
        }

        return await ShellingOutAsync(
            request.Path,
            async session =>
            {
                var root = session.Info.RootPath;
                if (
                    request.Context is { IsWiderThanDefault: true } wider
                    && await readWorkingCopyContextDiff(root, request.Path, wider, cancellationToken)
                        is { } written
                )
                {
                    return new DiffResponse(written, wider);
                }

                return new DiffResponse(
                    await readWorkingCopyDiff(root, request.Path, cancellationToken),
                    DiffContext.Default
                );
            },
            cancellationToken
        );
    }

    private static ErrorResponse? RefusedContext(DiffContext? context) =>
        context is { LinesAround: < 0 } negative
            ? new ErrorResponse(
                DaemonErrorKind.RequestRefused,
                $"{negative.LinesAround} lines of context is not an amount; ask for 0 or more, or the whole file."
            )
            : null;

    /// <remarks>
    /// Checked before SVN sees it because both mistakes mean something else to SVN: a negative
    /// <c>-c</c> is the same change reversed, and a path without its leading <c>/</c> would be
    /// joined to the root URL as though it had one.
    /// </remarks>
    private async Task<DaemonResponse> RevisionDiffAsync(
        RevisionDiffRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.Revision < 1)
        {
            return new ErrorResponse(
                DaemonErrorKind.RequestRefused,
                $"Revision {request.Revision} has no changes to show; revisions start at 1."
            );
        }

        if (!request.RepositoryPath.StartsWith('/'))
        {
            return new ErrorResponse(
                DaemonErrorKind.RequestRefused,
                $"'{request.RepositoryPath}' is not a repository path; they start with '/'."
            );
        }

        if (RefusedContext(request.Context) is { } refusal)
        {
            return refusal;
        }

        return await ShellingOutAsync(
            request.WorkingCopyPath,
            async session =>
            {
                var (root, repositoryRoot) = (session.Info.RootPath, session.Info.RepositoryRoot);
                if (
                    request.Context is { IsWiderThanDefault: true } wider
                    && await readRevisionContextDiff(
                        root,
                        repositoryRoot,
                        request.RepositoryPath,
                        request.Revision,
                        wider,
                        cancellationToken
                    )
                        is { } written
                )
                {
                    return new DiffResponse(written, wider);
                }

                return new DiffResponse(
                    await readRevisionDiff(
                        root,
                        repositoryRoot,
                        request.RepositoryPath,
                        request.Revision,
                        cancellationToken
                    ),
                    DiffContext.Default
                );
            },
            cancellationToken
        );
    }

    private Task<DaemonResponse> WorkingCopyRevisionAsync(
        WorkingCopyRevisionRequest request,
        CancellationToken cancellationToken
    ) =>
        ShellingOutAsync(
            request.Path,
            async session => new WorkingCopyRevisionResponse(
                await readBaseRevisionRange(session.Info.RootPath, request.Path, cancellationToken)
            ),
            cancellationToken
        );

    private Task<DaemonResponse> AddAsync(
        AddRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            request.Paths,
            "add",
            async session => new AddResponse(
                await scheduleAddition(session.Info.RootPath, request.Paths, cancellationToken)
            ),
            cancellationToken
        );

    private Task<DaemonResponse> RevertAsync(
        RevertRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            request.Paths,
            "revert",
            async session => new RevertResponse(
                await revertChanges(session.Info.RootPath, request.Paths, cancellationToken)
            ),
            cancellationToken
        );

    private Task<DaemonResponse> DeleteAsync(
        DeleteRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            request.Paths,
            "delete",
            async session => new DeleteResponse(
                await scheduleDeletion(session.Info.RootPath, request.Paths, cancellationToken)
            ),
            cancellationToken
        );

    /// <summary>
    /// Renames a node. Both paths are checked against the root rather than only the source: a
    /// destination in another working copy would make SVN record a copy from one repository into
    /// another, which is a different operation from the one anybody typed.
    /// </summary>
    private Task<DaemonResponse> MoveAsync(
        MoveRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            [request.Source, request.Destination],
            "move",
            async session =>
            {
                var outcome = await renameNode(
                    session.Info.RootPath,
                    request.Source,
                    request.Destination,
                    cancellationToken
                );

                return
                    MoveRefusal.Explain(outcome.Route, request.Source, request.Destination)
                        is { } refusal
                    ? new ErrorResponse(DaemonErrorKind.RequestRefused, refusal)
                    : new MoveResponse(outcome.Route, outcome.Notifications);
            },
            cancellationToken
        );

    private Task<DaemonResponse> CommitAsync(
        CommitRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            request.Paths,
            "commit",
            async session =>
            {
                var outcome = await commitChanges(
                    session.Info.RootPath,
                    request.Paths,
                    request.Message,
                    request.Scope,
                    cancellationToken
                );
                return new CommitResponse(outcome.Revision, outcome.Notifications);
            },
            cancellationToken
        );

    private Task<DaemonResponse> CommitSelectionAsync(
        CommitSelectionRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            request.Paths,
            "commit",
            session => selectionCommitter.CommitAsync(session, request, cancellationToken),
            cancellationToken
        );

    /// <summary>
    /// Takes the same route as the commands that write, because it is one: an update rewrites files
    /// the held index describes, so the index has to stop standing the moment the client has run.
    /// </summary>
    private Task<DaemonResponse> UpdateAsync(
        UpdateRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            [request.Path],
            "update",
            async session =>
            {
                var outcome = await bringUpToDate(
                    session.Info.RootPath,
                    request.Path,
                    cancellationToken
                );
                return new UpdateResponse(
                    outcome.Revision,
                    outcome.Conflicts,
                    outcome.SkippedPaths,
                    outcome.Notifications
                );
            },
            cancellationToken
        );

    /// <summary>
    /// Takes locks. A write like the rest: the lock token lands in wc.db and <c>sv st</c> reads it
    /// out of the held index as <c>K</c>, so an index that outlived the client would answer "you
    /// hold this" about a lock that had just been refused.
    /// </summary>
    private Task<DaemonResponse> LockAsync(
        LockRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            request.Paths,
            "lock",
            async session =>
            {
                var outcome = await acquireLocks(
                    session.Info.RootPath,
                    request.Paths,
                    request.Comment,
                    request.Foreign,
                    cancellationToken
                );
                return new LockResponse(outcome.Notifications, outcome.Refusals);
            },
            cancellationToken
        );

    private Task<DaemonResponse> UnlockAsync(
        UnlockRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            request.Paths,
            "unlock",
            async session =>
            {
                var outcome = await releaseLocks(
                    session.Info.RootPath,
                    request.Paths,
                    request.Foreign,
                    cancellationToken
                );
                return new UnlockResponse(outcome.Notifications, outcome.Refusals);
            },
            cancellationToken
        );

    /// <summary>
    /// Marks conflicts resolved. A write like the rest, and one of the larger ones: resolving to
    /// anything but the working version rewrites the file on disk, and the conflict flag it clears
    /// lives in wc.db where the held index read it as <c>C</c>.
    /// </summary>
    private Task<DaemonResponse> ResolveAsync(
        ResolveRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            request.Paths,
            "resolve",
            async session =>
            {
                var outcome = await resolveConflicts(
                    session.Info.RootPath,
                    request.Paths,
                    request.Resolution,
                    cancellationToken
                );
                return new ResolveResponse(outcome.ResolvedPaths, outcome.Refusals);
            },
            cancellationToken
        );

    /// <summary>
    /// Unwedges a working copy. A write like the rest, and the one that most has to invalidate the
    /// held index: what it clears is the state that was making every other write fail.
    /// </summary>
    /// <remarks>
    /// The request names a path and the cleanup runs at that path's root, because cleanup's two
    /// halves scope differently — see <see cref="SvnCleanupCommand"/>. So there is no target to
    /// check against the root here, only a root to find.
    /// </remarks>
    private Task<DaemonResponse> CleanupAsync(
        CleanupRequest request,
        CancellationToken cancellationToken
    ) =>
        WritingAsync(
            [request.Path],
            "cleanup",
            async session =>
            {
                var outcome = await cleanUpWorkingCopy(session.Info.RootPath, cancellationToken);
                return new CleanupResponse(
                    outcome.ReleasedWriteLocks,
                    outcome.FinishedOperations,
                    outcome.RemainingWriteLocks
                );
            },
            cancellationToken
        );

    /// <summary>
    /// Runs one of the commands that write to the working copy. What they share is here: there has
    /// to be at least one target, every target has to live in the same working copy, and the held
    /// index stops standing the moment the client has been run.
    /// </summary>
    private async Task<DaemonResponse> WritingAsync(
        IReadOnlyList<string> paths,
        string operation,
        Func<WorkingCopySession, Task<DaemonResponse>> write,
        CancellationToken cancellationToken
    )
    {
        if (paths.Count == 0)
        {
            return new ErrorResponse(
                DaemonErrorKind.Internal,
                $"A {operation} request named no paths, and there is no safe default for one."
            );
        }

        try
        {
            var session = await sessions.ForAsync(paths[0], cancellationToken);
            if (
                WorkingCopyTargets.FirstOutside(
                    session.Info.RootPath,
                    paths,
                    ContainingRoot.PlatformComparison
                ) is
                { } outside
            )
            {
                return new ErrorResponse(
                    DaemonErrorKind.NotAWorkingCopy,
                    $"'{outside}' is not in the working copy at '{session.Info.RootPath}'. "
                        + $"One {operation} covers one working copy."
                );
            }

            try
            {
                return await write(session);
            }
            finally
            {
                // Even when the client failed: `svn add` given three paths can schedule two of them
                // and refuse the third, and an index that survived that would be wrong about both.
                session.Invalidate();
            }
        }
        catch (WcDbException ex)
        {
            return new ErrorResponse(Translate(ex.Failure), ex.Message);
        }
        catch (SvnCommandException ex)
        {
            return new ErrorResponse(DaemonErrorKind.SvnCommandFailed, ex.Message);
        }
    }

    /// <summary>
    /// Runs one of the shell-out commands (D6) against the working copy containing a path. The
    /// session is what turns a path into a root to run <c>svn</c> in, and what makes "this is not
    /// a working copy" the same answer here as it is for status.
    /// </summary>
    private async Task<DaemonResponse> ShellingOutAsync(
        string path,
        Func<WorkingCopySession, Task<DaemonResponse>> answer,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await answer(await sessions.ForAsync(path, cancellationToken));
        }
        catch (WcDbException ex)
        {
            return new ErrorResponse(Translate(ex.Failure), ex.Message);
        }
        catch (SvnCommandException ex)
        {
            return new ErrorResponse(DaemonErrorKind.SvnCommandFailed, ex.Message);
        }
    }

    private DaemonResponse DaemonInfo() =>
        new DaemonInfoResponse(
            Version,
            (clock.GetUtcNow() - _startedAt).TotalSeconds,
            [
                .. sessions.All.Select(session => new WatchedWorkingCopy(
                    session.Info.RootPath,
                    session.EntryCount,
                    session.WatcherState
                )),
            ]
        );

    private DaemonResponse Shutdown()
    {
        shutdown.RequestShutdown();
        return new AcknowledgedResponse();
    }

    private static DaemonErrorKind Translate(WcDbFailure failure) =>
        failure switch
        {
            WcDbFailure.NotAWorkingCopy => DaemonErrorKind.NotAWorkingCopy,
            _ => DaemonErrorKind.WorkingCopyUnreadable,
        };
}
