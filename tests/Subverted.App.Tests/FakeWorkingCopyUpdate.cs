using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>Records every update it is asked for; answers as queued, or with a clean update to r1.</summary>
internal sealed class FakeWorkingCopyUpdate : IWorkingCopyUpdate
{
    private readonly Queue<(Func<DaemonResponse> Respond, Task HeldUntil)> _answers = new();

    public List<string> Updated { get; } = [];

    public FakeWorkingCopyUpdate Answers(DaemonResponse response, Task? heldUntil = null)
    {
        _answers.Enqueue((() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyUpdate IsUnreachable(string message = "connection refused")
    {
        _answers.Enqueue(
            (
                () => throw new DaemonUnreachableException(message, new IOException(message)),
                Task.CompletedTask
            )
        );
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
}
