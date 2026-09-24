using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>Answers at once until told to hold, then keeps every answer back until released.</summary>
internal sealed class HeldWorkingCopyStatus : IWorkingCopyStatus
{
    private TaskCompletionSource? _gate;

    public int Reads { get; private set; }

    public void Hold() => _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release()
    {
        _gate?.TrySetResult();
        _gate = null;
    }

    public async Task<DaemonResponse> ReadAsync(
        string workingCopyPath,
        ListedNodes listed,
        Guid? heldScan,
        CancellationToken cancellationToken
    )
    {
        Reads++;
        if (_gate is { } gate)
        {
            await gate.Task;
        }

        return Entries.Listing();
    }
}
