using Subverted.App.ViewModels;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>Asks the daemon to update a path, or what updating it would bring, starting it if needed.</summary>
public sealed class DaemonWorkingCopyUpdate(DaemonChannel channel) : IWorkingCopyUpdate
{
    public Task<DaemonResponse> UpdateAsync(string path, CancellationToken cancellationToken) =>
        DaemonQuestion.AskAsync(channel, new UpdateRequest(path), cancellationToken);

    public Task<DaemonResponse> IncomingAsync(string path, CancellationToken cancellationToken) =>
        DaemonQuestion.AskAsync(channel, new IncomingRequest(path), cancellationToken);
}
