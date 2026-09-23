using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>
/// Answers each question with the next queued answer, then repeats the last. An answer can be
/// held back until the test releases it, which is how an old question is made to land late.
/// </summary>
internal sealed class FakeWorkingCopyDiff : IWorkingCopyDiff
{
    private readonly Queue<Answer> _answers = new();
    private Answer _last = new(() => new DiffResponse(""), Task.CompletedTask);

    public List<string> Paths { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public FakeWorkingCopyDiff Answers(DaemonResponse response, Task? heldUntil = null)
    {
        _answers.Enqueue(new Answer(() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyDiff Answers(string unifiedDiff, Task? heldUntil = null) =>
        Answers(new DiffResponse(unifiedDiff), heldUntil);

    public FakeWorkingCopyDiff IsUnreachable(
        string message = "connection refused",
        Task? heldUntil = null
    )
    {
        _answers.Enqueue(
            new Answer(
                () => throw new DaemonUnreachableException(message, new IOException(message)),
                heldUntil ?? Task.CompletedTask
            )
        );
        return this;
    }

    /// <summary>Never answers, and throws once its question is cancelled, as the real channel does.</summary>
    public FakeWorkingCopyDiff HoldsUntilCancelled()
    {
        _answers.Enqueue(new Answer(() => new DiffResponse(""), HeldUntil: null));
        return this;
    }

    /// <summary>Otherwise ignores cancellation on purpose: a real answer can already be on the wire.</summary>
    public async Task<DaemonResponse> ReadAsync(string path, CancellationToken cancellationToken)
    {
        Paths.Add(path);
        Tokens.Add(cancellationToken);
        if (_answers.TryDequeue(out var next))
        {
            _last = next;
        }

        var answer = _last;
        await (answer.HeldUntil ?? Task.Delay(Timeout.Infinite, cancellationToken));
        return answer.Respond();
    }

    /// <param name="HeldUntil"><c>null</c> for an answer that waits until it is cancelled.</param>
    private sealed record Answer(Func<DaemonResponse> Respond, Task? HeldUntil);
}
