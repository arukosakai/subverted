using System.Net.Sockets;
using Subverted.App.ViewModels;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>Sends a ticked set to the daemon as one request, starting it from beside the app if needed.</summary>
public sealed class DaemonWorkingCopyCommit(DaemonChannel channel) : IWorkingCopyCommit
{
    public async Task<DaemonResponse> CommitAsync(
        IReadOnlyList<string> paths,
        string message,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await channel.SendAsync(
                new CommitSelectionRequest(paths, message),
                cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is SocketException
                        or IOException
                        or TimeoutException
                        or ProtocolException
                        or UnauthorizedAccessException
            )
        {
            throw new DaemonUnreachableException(exception.Message, exception);
        }
    }
}
