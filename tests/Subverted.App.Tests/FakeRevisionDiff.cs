using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>Answers each question with the next queued answer, then an empty diff.</summary>
internal sealed class FakeRevisionDiff : IRevisionDiff
{
    private readonly Queue<Answer> _answers = new();

    public List<(string WorkingCopyPath, string RepositoryPath, long Revision)> Questions { get; } =
    [];

    public List<CancellationToken> Tokens { get; } = [];

    /// <summary>The context each question asked for, beside <see cref="Questions"/>.</summary>
    public List<DiffContext?> Contexts { get; } = [];

    public FakeRevisionDiff Answers(DaemonResponse response, Task? heldUntil = null)
    {
        _answers.Enqueue(new Answer(() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeRevisionDiff Answers(string unifiedDiff, Task? heldUntil = null) =>
        Answers(new DiffResponse(unifiedDiff), heldUntil);

    public FakeRevisionDiff IsUnreachable(
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
    public FakeRevisionDiff HoldsUntilCancelled()
    {
        _answers.Enqueue(new Answer(() => new DiffResponse(""), HeldUntil: null));
        return this;
    }

    /// <summary>Otherwise ignores cancellation on purpose: a real answer can already be on the wire.</summary>
    public async Task<DaemonResponse> ReadAsync(
        string workingCopyPath,
        string repositoryPath,
        long revision,
        DiffContext? context,
        CancellationToken cancellationToken
    )
    {
        Questions.Add((workingCopyPath, repositoryPath, revision));
        Contexts.Add(context);
        Tokens.Add(cancellationToken);
        var answer = _answers.TryDequeue(out var next)
            ? next
            : new Answer(() => new DiffResponse(""), Task.CompletedTask);
        await (answer.HeldUntil ?? Task.Delay(Timeout.Infinite, cancellationToken));
        return answer.Respond();
    }

    /// <param name="HeldUntil"><c>null</c> for an answer that waits until it is cancelled.</param>
    private sealed record Answer(Func<DaemonResponse> Respond, Task? HeldUntil);
}
