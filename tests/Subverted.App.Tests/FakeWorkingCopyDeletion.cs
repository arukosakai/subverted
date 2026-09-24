using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>
/// Answers listings from a queue, repeating the last; records every delete it is asked for and
/// answers those as queued, or with an empty success.
/// </summary>
internal sealed class FakeWorkingCopyDeletion : IWorkingCopyDeletion
{
    private readonly Queue<(Func<DaemonResponse> Respond, Task HeldUntil)> _listings = new();
    private readonly Queue<(Func<DaemonResponse> Respond, Task HeldUntil)> _deletes = new();
    private (Func<DaemonResponse> Respond, Task HeldUntil) _lastListing = (
        () => Entries.Listing(),
        Task.CompletedTask
    );

    public List<string> Listed { get; } = [];

    public List<string> Deleted { get; } = [];

    public FakeWorkingCopyDeletion Lists(DaemonResponse response, Task? heldUntil = null)
    {
        _listings.Enqueue((() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyDeletion ListingIsUnreachable(string message = "connection refused")
    {
        _listings.Enqueue((() => throw Unreachable(message), Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyDeletion Answers(DaemonResponse response, Task? heldUntil = null)
    {
        _deletes.Enqueue((() => response, heldUntil ?? Task.CompletedTask));
        return this;
    }

    public FakeWorkingCopyDeletion DeleteIsUnreachable(string message = "connection refused")
    {
        _deletes.Enqueue((() => throw Unreachable(message), Task.CompletedTask));
        return this;
    }

    public async Task<DaemonResponse> ListAsync(string path, CancellationToken cancellationToken)
    {
        Listed.Add(path);
        if (_listings.TryDequeue(out var next))
        {
            _lastListing = next;
        }

        await _lastListing.HeldUntil;
        return _lastListing.Respond();
    }

    public async Task<DaemonResponse> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        Deleted.Add(path);
        var (respond, heldUntil) = _deletes.TryDequeue(out var next)
            ? next
            : (() => new DeleteResponse(""), Task.CompletedTask);
        await heldUntil;
        return respond();
    }

    private static DaemonUnreachableException Unreachable(string message) =>
        new(message, new IOException(message));
}
