using Subverted.Core;

namespace Subverted.Daemon.Tests;

/// <summary>
/// An incremental reader that records what it was asked to apply. Recording the *paths* is the
/// point: a session that quietly hands over an empty set, or the wrong set, still returns a
/// plausible list, and only the request itself shows which.
/// </summary>
internal sealed class FakeIncrementalScan : IIncrementalScan
{
    public int Applications { get; private set; }

    /// <summary>The changed paths of each call, in order.</summary>
    public List<IReadOnlySet<string>> Requested { get; } = [];

    /// <summary>
    /// What to answer. Returning <see langword="null"/> is the reader saying "rescan", which the
    /// session must honour.
    /// </summary>
    public Func<
        IReadOnlyList<WorkingCopyEntry>,
        IReadOnlySet<string>,
        IReadOnlyList<WorkingCopyEntry>?
    > Apply { get; set; } = (held, _) => held;

    public Task<IReadOnlyList<WorkingCopyEntry>?> TryApplyAsync(
        IReadOnlyList<WorkingCopyEntry> held,
        IReadOnlySet<string> changedRelPaths,
        CancellationToken cancellationToken
    )
    {
        Applications++;
        Requested.Add(changedRelPaths);
        return Task.FromResult(Apply(held, changedRelPaths));
    }
}
