using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>Records every resolve it is asked for; answers as queued, or with the path resolved.</summary>
internal sealed class FakeWorkingCopyResolve : IWorkingCopyResolve
{
    private readonly Queue<(Func<DaemonResponse> Respond, Task HeldUntil)> _answers = new();

    public List<(string Path, ConflictResolution Resolution)> Resolved { get; } = [];

    public FakeWorkingCopyResolve Answers(DaemonResponse response, Task? heldUntil = null)
    {
        _answers.Enqueue((() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyResolve IsUnreachable(string message = "connection refused")
    {
        _answers.Enqueue(
            (
                () => throw new DaemonUnreachableException(message, new IOException(message)),
                Task.CompletedTask
            )
        );
        return this;
    }

    public async Task<DaemonResponse> ResolveAsync(
        string path,
        ConflictResolution resolution,
        CancellationToken cancellationToken
    )
    {
        Resolved.Add((path, resolution));
        var (respond, heldUntil) = _answers.TryDequeue(out var next)
            ? next
            : (() => new ResolveResponse(["resolved"], []), Task.CompletedTask);
        await heldUntil;
        return respond();
    }
}
