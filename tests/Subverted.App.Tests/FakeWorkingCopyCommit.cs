using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>
/// Records every commit it is asked for and answers with the next queued answer, committing at r1
/// when none is queued. An answer can be held back, which is how a commit is kept in flight.
/// </summary>
internal sealed class FakeWorkingCopyCommit : IWorkingCopyCommit
{
    private readonly Queue<(Func<DaemonResponse> Respond, Task HeldUntil)> _answers = new();

    public List<(IReadOnlyList<string> Paths, string Message)> Commits { get; } = [];

    public static CommitSelectionResponse Committed(long? revision = 1) =>
        new(revision, new SelectionSchedule([], [], []), "");

    public FakeWorkingCopyCommit Answers(DaemonResponse response, Task? heldUntil = null)
    {
        _answers.Enqueue((() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyCommit IsUnreachable(string message = "connection refused")
    {
        _answers.Enqueue(
            (
                () => throw new DaemonUnreachableException(message, new IOException(message)),
                Task.CompletedTask
            )
        );
        return this;
    }

    public async Task<DaemonResponse> CommitAsync(
        IReadOnlyList<string> paths,
        string message,
        CancellationToken cancellationToken
    )
    {
        Commits.Add((paths, message));
        var (respond, heldUntil) = _answers.TryDequeue(out var next)
            ? next
            : (() => Committed(), Task.CompletedTask);
        await heldUntil;
        return respond();
    }
}
