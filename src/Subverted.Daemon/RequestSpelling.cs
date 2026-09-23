using Subverted.Protocol;

namespace Subverted.Daemon;

/// <summary>
/// Every path a request carries, in one spelling. Windows has two for most folders — the 8.3 short
/// form <c>%TEMP%</c> hands out and the long form a file dialog hands out — and the daemon compares
/// paths as strings, so the same working copy asked about in both would be two strangers.
/// </summary>
public static class RequestSpelling
{
    /// <summary>Every request type, so a test can prove a new one was not forgotten here.</summary>
    public static readonly IReadOnlySet<Type> Covered = new HashSet<Type>
    {
        typeof(StatusRequest),
        typeof(LogRequest),
        typeof(DiffRequest),
        typeof(AddRequest),
        typeof(RevertRequest),
        typeof(DeleteRequest),
        typeof(MoveRequest),
        typeof(CommitRequest),
        typeof(UpdateRequest),
        typeof(LockRequest),
        typeof(UnlockRequest),
        typeof(ResolveRequest),
        typeof(CleanupRequest),
        typeof(DaemonInfoRequest),
        typeof(ShutdownRequest),
    };

    /// <param name="respell">Maps one absolute path to its one spelling.</param>
    /// <returns>The request with every path respelled and everything else as it was.</returns>
    public static DaemonRequest Respell(DaemonRequest request, Func<string, string> respell) =>
        request switch
        {
            StatusRequest status => status with
            {
                WorkingCopyPath = respell(status.WorkingCopyPath),
                Scope = status.Scope is { } scope ? All(scope, respell) : null,
            },
            LogRequest log => log with { Path = respell(log.Path) },
            DiffRequest diff => diff with { Path = respell(diff.Path) },
            AddRequest add => add with { Paths = All(add.Paths, respell) },
            RevertRequest revert => revert with { Paths = All(revert.Paths, respell) },
            DeleteRequest delete => delete with { Paths = All(delete.Paths, respell) },
            MoveRequest move => move with
            {
                Source = respell(move.Source),
                Destination = respell(move.Destination),
            },
            CommitRequest commit => commit with { Paths = All(commit.Paths, respell) },
            UpdateRequest update => update with { Path = respell(update.Path) },
            LockRequest take => take with { Paths = All(take.Paths, respell) },
            UnlockRequest release => release with { Paths = All(release.Paths, respell) },
            ResolveRequest resolve => resolve with { Paths = All(resolve.Paths, respell) },
            CleanupRequest cleanup => cleanup with { Path = respell(cleanup.Path) },
            _ => request,
        };

    private static IReadOnlyList<string> All(
        IReadOnlyList<string> paths,
        Func<string, string> respell
    ) => [.. paths.Select(respell)];
}
