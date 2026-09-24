using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>Holds the first answer until released, so a test can act while it is in flight.</summary>
internal sealed class GatedWorkingCopyStatus : IWorkingCopyStatus
{
    private readonly TaskCompletionSource _gate = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public int Reads { get; private set; }

    public void Release() => _gate.TrySetResult();

    public async Task<DaemonResponse> ReadAsync(
        string workingCopyPath,
        ListedNodes listed,
        Guid? heldScan,
        CancellationToken cancellationToken
    )
    {
        Reads++;
        await _gate.Task;
        return Entries.Listing();
    }
}
