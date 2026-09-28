using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>
/// Records every update and every incoming check it is asked for; answers as queued, or with a
/// clean update to r1 and nothing incoming.
/// </summary>
internal sealed class FakeWorkingCopyUpdate : IWorkingCopyUpdate
{
    private readonly Queue<(Func<DaemonResponse> Respond, Task HeldUntil)> _answers = new();
    private readonly Queue<(Func<DaemonResponse> Respond, Task HeldUntil)> _incoming = new();

    public List<string> Updated { get; } = [];

    public List<string> AskedIncoming { get; } = [];

    public FakeWorkingCopyUpdate Answers(DaemonResponse response, Task? heldUntil = null)
    {
        _answers.Enqueue((() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyUpdate IsUnreachable(string message = "connection refused")
    {
        _answers.Enqueue((() => throw Unreachable(message), Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyUpdate AnswersIncoming(DaemonResponse response, Task? heldUntil = null)
    {
        _incoming.Enqueue((() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyUpdate AnswersIncoming(params IncomingChange[] changes) =>
        AnswersIncoming(new IncomingResponse(new IncomingChanges(9, changes)));

    public FakeWorkingCopyUpdate IncomingIsUnreachable(string message = "connection refused")
    {
        _incoming.Enqueue((() => throw Unreachable(message), Task.CompletedTask));
        return this;
    }

    public async Task<DaemonResponse> UpdateAsync(string path, CancellationToken cancellationToken)
    {
        Updated.Add(path);
        var (respond, heldUntil) = _answers.TryDequeue(out var next)
            ? next
            : (() => new UpdateResponse(1, 0, 0, "At revision 1."), Task.CompletedTask);
        await heldUntil;
        return respond();
    }

    public async Task<DaemonResponse> IncomingAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        AskedIncoming.Add(path);
        var (respond, heldUntil) = _incoming.TryDequeue(out var next)
            ? next
            : (() => new IncomingResponse(new IncomingChanges(1, [])), Task.CompletedTask);
        await heldUntil;
        return respond();
    }

    private static DaemonUnreachableException Unreachable(string message) =>
        new(message, new IOException(message));
}
