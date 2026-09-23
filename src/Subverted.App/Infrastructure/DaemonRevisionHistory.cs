using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>Asks the daemon for pages of history and for the copy's BASE range.</summary>
public sealed class DaemonRevisionHistory(DaemonChannel channel) : IRevisionHistory
{
    public Task<DaemonResponse> ReadAsync(
        string path,
        HistoryStart start,
        int limit,
        CancellationToken cancellationToken
    ) => DaemonQuestion.AskAsync(channel, new LogRequest(path, limit, start), cancellationToken);

    public Task<DaemonResponse> ReadBaseRangeAsync(
        string path,
        CancellationToken cancellationToken
    ) => DaemonQuestion.AskAsync(channel, new WorkingCopyRevisionRequest(path), cancellationToken);
}
