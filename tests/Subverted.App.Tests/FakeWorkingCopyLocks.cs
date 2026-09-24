using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>Records every lock and unlock it is asked for; answers as queued, or as SVN does for one file done.</summary>
internal sealed class FakeWorkingCopyLocks : IWorkingCopyLocks
{
    private readonly Queue<(Func<DaemonResponse> Respond, Task HeldUntil)> _answers = new();

    public List<string> Locked { get; } = [];

    public List<string> Unlocked { get; } = [];

    public FakeWorkingCopyLocks Answers(DaemonResponse response, Task? heldUntil = null)
    {
        _answers.Enqueue((() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyLocks IsUnreachable(string message = "connection refused")
    {
        _answers.Enqueue(
            (
                () => throw new DaemonUnreachableException(message, new IOException(message)),
                Task.CompletedTask
            )
        );
        return this;
    }

    public Task<DaemonResponse> LockAsync(string path, CancellationToken cancellationToken)
    {
        Locked.Add(path);
        return AnswerAsync(() => new LockResponse($"'{path}' locked by user 'keiichi'.", []));
    }

    public Task<DaemonResponse> UnlockAsync(string path, CancellationToken cancellationToken)
    {
        Unlocked.Add(path);
        return AnswerAsync(() => new UnlockResponse($"'{path}' unlocked.", []));
    }

    private async Task<DaemonResponse> AnswerAsync(Func<DaemonResponse> byDefault)
    {
        var (respond, heldUntil) = _answers.TryDequeue(out var next)
            ? next
            : (byDefault, Task.CompletedTask);
        await heldUntil;
        return respond();
    }
}
