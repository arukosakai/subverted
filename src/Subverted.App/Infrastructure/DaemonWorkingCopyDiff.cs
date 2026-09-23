using System.Net.Sockets;
using Subverted.App.ViewModels;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>Asks the daemon for a path's local changes, starting it from beside the app if needed.</summary>
public sealed class DaemonWorkingCopyDiff(DaemonChannel channel) : IWorkingCopyDiff
{
    public async Task<DaemonResponse> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await channel.SendAsync(new DiffRequest(path), cancellationToken);
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
