using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>Records every revert it is asked for; answers as queued, or with an empty success.</summary>
internal sealed class FakeWorkingCopyRevert : IWorkingCopyRevert
{
    private readonly Queue<(Func<DaemonResponse> Respond, Task HeldUntil)> _answers = new();

    public List<string> Reverted { get; } = [];

    public FakeWorkingCopyRevert Answers(DaemonResponse response, Task? heldUntil = null)
    {
        _answers.Enqueue((() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyRevert IsUnreachable(string message = "connection refused")
    {
        _answers.Enqueue(
            (
                () => throw new DaemonUnreachableException(message, new IOException(message)),
                Task.CompletedTask
            )
        );
        return this;
    }

    public async Task<DaemonResponse> RevertAsync(string path, CancellationToken cancellationToken)
    {
        Reverted.Add(path);
        var (respond, heldUntil) = _answers.TryDequeue(out var next)
            ? next
            : (() => new RevertResponse(""), Task.CompletedTask);
        await heldUntil;
        return respond();
    }
}
