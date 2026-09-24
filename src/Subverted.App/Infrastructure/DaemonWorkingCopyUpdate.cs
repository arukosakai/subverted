using Subverted.App.ViewModels;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>Asks the daemon to update a path, starting it from beside the app if needed.</summary>
public sealed class DaemonWorkingCopyUpdate(DaemonChannel channel) : IWorkingCopyUpdate
{
    public Task<DaemonResponse> UpdateAsync(string path, CancellationToken cancellationToken) =>
        DaemonQuestion.AskAsync(channel, new UpdateRequest(path), cancellationToken);
}
