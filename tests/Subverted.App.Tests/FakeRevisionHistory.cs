using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>
/// Answers each page with the next queued answer, then an empty page; the BASE range likewise,
/// then "no range". Either can be held back until the test releases it, which is how an answer
/// for an earlier path is made to land after a later one.
/// </summary>
internal sealed class FakeRevisionHistory : IRevisionHistory
{
    private readonly Queue<Answer> _pages = new();
    private readonly Queue<Answer> _ranges = new();

    public List<(string Path, HistoryStart Start, int Limit)> Pages { get; } = [];

    public List<string> Ranges { get; } = [];

    public FakeRevisionHistory Page(DaemonResponse response, Task? heldUntil = null)
    {
        _pages.Enqueue(new Answer(() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeRevisionHistory Page(params long[] revisions) =>
        Page(new LogResponse([.. revisions.Select(Revisions.Entry)]));

    public FakeRevisionHistory PageIsUnreachable(string message = "connection refused")
    {
        _pages.Enqueue(
            new Answer(
                () => throw new DaemonUnreachableException(message, new IOException(message)),
                Task.CompletedTask
            )
        );
        return this;
    }

    /// <summary>Never answers, and throws once its question is cancelled, as the real channel does.</summary>
    public FakeRevisionHistory PageHoldsUntilCancelled()
    {
        _pages.Enqueue(new Answer(() => new LogResponse([]), HeldUntil: null));
        return this;
    }

    public FakeRevisionHistory RangeHoldsUntilCancelled()
    {
        _ranges.Enqueue(new Answer(() => new WorkingCopyRevisionResponse(null), HeldUntil: null));
        return this;
    }

    public FakeRevisionHistory Range(DaemonResponse response, Task? heldUntil = null)
    {
        _ranges.Enqueue(new Answer(() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeRevisionHistory Range(long lowest, long highest) =>
        Range(new WorkingCopyRevisionResponse(new BaseRevisionRange(lowest, highest)));

    public FakeRevisionHistory RangeIsUnreachable()
    {
        _ranges.Enqueue(
            new Answer(
                () => throw new DaemonUnreachableException("gone", new IOException("gone")),
                Task.CompletedTask
            )
        );
        return this;
    }

    /// <summary>Ignores cancellation on purpose: a real answer can already be on the wire.</summary>
    public async Task<DaemonResponse> ReadAsync(
        string path,
        HistoryStart start,
        int limit,
        CancellationToken cancellationToken
    )
    {
        Pages.Add((path, start, limit));
        var answer = _pages.TryDequeue(out var next)
            ? next
            : new Answer(() => new LogResponse([]), Task.CompletedTask);
        await (answer.HeldUntil ?? Task.Delay(Timeout.Infinite, cancellationToken));
        return answer.Respond();
    }

    public async Task<DaemonResponse> ReadBaseRangeAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        Ranges.Add(path);
        var answer = _ranges.TryDequeue(out var next)
            ? next
            : new Answer(() => new WorkingCopyRevisionResponse(null), Task.CompletedTask);
        await (answer.HeldUntil ?? Task.Delay(Timeout.Infinite, cancellationToken));
        return answer.Respond();
    }

    /// <param name="HeldUntil"><c>null</c> for an answer that waits until it is cancelled.</param>
    private sealed record Answer(Func<DaemonResponse> Respond, Task? HeldUntil);
}
