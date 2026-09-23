using System.Text;
using Subverted.Core;
using Subverted.Protocol;
using Subverted.Svn;

namespace Subverted.Daemon;

/// <summary>
/// Records what happened to each ticked node and commits them, in one request — so what decides a
/// commit lives here and not in each front-end. Moves first, then additions, then deletions, then
/// the commit, and every step's schedule is left standing if a later one fails.
/// </summary>
/// <remarks>
/// Planned from the session's current status rather than a fresh scan. That is safe because no
/// step acts on the status alone: a rename's route is read off disk, and a deletion is recorded
/// without <c>--force</c>, so a file that came back with edits is refused rather than unlinked.
/// </remarks>
public sealed class SelectionCommitter(
    RenameNode renameNode,
    ScheduleAddition scheduleAddition,
    RecordDeletion recordDeletion,
    CommitChanges commitChanges
)
{
    /// <returns>
    /// <see cref="CommitSelectionResponse"/> when it all went through;
    /// <see cref="SelectionNotCommittedResponse"/> when a step that writes ran and something then
    /// failed; <see cref="ErrorResponse"/> only when nothing was written at all.
    /// </returns>
    /// <exception cref="WcDbException">The status the plan is made from could not be read.</exception>
    /// <exception cref="SvnCommandException">
    /// The same, from the CLI fallback. Nothing has been written when either is thrown.
    /// </exception>
    public async Task<DaemonResponse> CommitAsync(
        WorkingCopySession session,
        CommitSelectionRequest request,
        CancellationToken cancellationToken
    )
    {
        var root = session.Info.RootPath;
        var comparison = ContainingRoot.PlatformComparison;
        var current = await session.CurrentAsync(cancellationToken);
        var plan = CommitSelectionPlanner.Plan(
            [.. request.Paths.Select(path => TargetCoverage.RelativeTo(root, path))],
            current.Entries,
            current.UnrecordedMoves,
            comparison
        );

        if (plan.Refusals.Count > 0)
        {
            return new ErrorResponse(
                DaemonErrorKind.RequestRefused,
                string.Join(Environment.NewLine, plan.Refusals)
            );
        }

        var run = new Run(root);
        return await MoveAsync(run, plan, cancellationToken)
            ?? await AddAsync(run, session, plan, comparison, cancellationToken)
            ?? await DeleteAsync(run, plan, cancellationToken)
            ?? await SendAsync(run, plan, request.Message, cancellationToken);
    }

    /// <returns>Why it stopped, or <see langword="null"/> to carry on.</returns>
    private async Task<DaemonResponse?> MoveAsync(
        Run run,
        CommitSelectionPlan plan,
        CancellationToken cancellationToken
    )
    {
        foreach (var move in plan.Moves)
        {
            var source = run.Absolute(move.FromRelPath);
            var destination = run.Absolute(move.ToRelPath);
            MoveOutcome outcome;
            try
            {
                outcome = await renameNode(run.Root, source, destination, cancellationToken);
            }
            catch (SvnCommandException ex)
            {
                return run.Stopped(SelectionStep.Move, ex.Message);
            }

            // A refusing route runs no client, so before any other write it means nothing happened.
            if (MoveRefusal.Explain(outcome.Route, source, destination) is { } refusal)
            {
                return run.HasWritten
                    ? run.Stopped(SelectionStep.Move, refusal)
                    : new ErrorResponse(DaemonErrorKind.RequestRefused, refusal);
            }

            run.Record(outcome.Notifications);
            run.Moved.Add(new RecordedMove(move.FromRelPath, move.ToRelPath));
        }

        return null;
    }

    private async Task<DaemonResponse?> AddAsync(
        Run run,
        WorkingCopySession session,
        CommitSelectionPlan plan,
        StringComparison comparison,
        CancellationToken cancellationToken
    )
    {
        if (plan.Additions.Count == 0)
        {
            return null;
        }

        try
        {
            run.Wrote();
            run.Record(
                await scheduleAddition(
                    run.Root,
                    [.. plan.Additions.Select(entry => run.Absolute(entry.RelPath))],
                    cancellationToken
                )
            );
            run.Added.AddRange(plan.Additions.Select(entry => entry.RelPath));
            run.Added.AddRange(
                await ScheduledBeneathAsync(session, plan.Additions, comparison, cancellationToken)
            );
        }
        catch (Exception ex) when (ex is SvnCommandException or WcDbException)
        {
            return run.Stopped(SelectionStep.Addition, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// What adding the unversioned directories scheduled beneath them. <c>svn add</c> recurses and
    /// skips what is ignored, and a commit at <c>--depth empty</c> takes only what it names — so
    /// without these the directory would reach the server empty and its contents stay local.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ScheduledBeneathAsync(
        WorkingCopySession session,
        IReadOnlyList<WorkingCopyEntry> additions,
        StringComparison comparison,
        CancellationToken cancellationToken
    )
    {
        var directories = additions
            .Where(entry => entry.Kind == NodeKind.Directory)
            .Select(entry => entry.RelPath)
            .ToList();
        if (directories.Count == 0)
        {
            return [];
        }

        session.Invalidate();
        var afterAdding = await session.CurrentAsync(cancellationToken);
        return
        [
            .. afterAdding
                .Entries.Where(entry =>
                    entry.Status == NodeStatus.Added
                    && directories.Any(directory =>
                        TargetCoverage.Below(directory, entry.RelPath, comparison)
                    )
                )
                .Select(entry => entry.RelPath)
                .OrderBy(relPath => relPath, StringComparer.Ordinal),
        ];
    }

    private async Task<DaemonResponse?> DeleteAsync(
        Run run,
        CommitSelectionPlan plan,
        CancellationToken cancellationToken
    )
    {
        if (plan.Deletions.Count == 0)
        {
            return null;
        }

        try
        {
            run.Wrote();
            run.Record(
                await recordDeletion(
                    run.Root,
                    [.. plan.Deletions.Select(run.Absolute)],
                    cancellationToken
                )
            );
            run.Deleted.AddRange(plan.Deletions);
        }
        catch (SvnCommandException ex)
        {
            return run.Stopped(SelectionStep.Deletion, ex.Message);
        }

        return null;
    }

    private async Task<DaemonResponse> SendAsync(
        Run run,
        CommitSelectionPlan plan,
        string message,
        CancellationToken cancellationToken
    )
    {
        var beneathAddedDirectories = run.Added.Except(
            plan.Additions.Select(entry => entry.RelPath)
        );
        IReadOnlyList<string> targets =
        [
            .. plan.CommitTargets.Concat(beneathAddedDirectories).Select(run.Absolute),
        ];

        try
        {
            var outcome = await commitChanges(
                run.Root,
                targets,
                message,
                CommitScope.ExactlyTheseNodes,
                cancellationToken
            );
            run.Record(outcome.Notifications);
            return new CommitSelectionResponse(outcome.Revision, run.Schedule, run.Notifications);
        }
        catch (SvnCommandException ex)
        {
            return run.Stopped(SelectionStep.Commit, ex.Message);
        }
    }

    /// <summary>What one request has done so far, so a failure at any step can say it.</summary>
    private sealed class Run(string root)
    {
        private readonly StringBuilder _notifications = new();

        public string Root { get; } = root;

        public List<string> Added { get; } = [];

        public List<string> Deleted { get; } = [];

        public List<RecordedMove> Moved { get; } = [];

        /// <summary>A client that writes has been started, whether or not it finished.</summary>
        public bool HasWritten { get; private set; }

        public string Notifications => _notifications.ToString();

        public SelectionSchedule Schedule => new([.. Added], [.. Deleted], [.. Moved]);

        public void Wrote() => HasWritten = true;

        public void Record(string notifications)
        {
            HasWritten = true;
            _notifications.Append(notifications);
        }

        public string Absolute(string relPath) =>
            relPath.Length == 0
                ? Root
                : Path.Combine(Root, relPath.Replace('/', Path.DirectorySeparatorChar));

        public SelectionNotCommittedResponse Stopped(SelectionStep step, string failure) =>
            new(Schedule, step, Notifications, failure);
    }
}
