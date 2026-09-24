using System.Net.Sockets;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>Asks the daemon to resolve a path, starting it from beside the app if needed.</summary>
public sealed class DaemonWorkingCopyResolve(DaemonChannel channel) : IWorkingCopyResolve
{
    public async Task<DaemonResponse> ResolveAsync(
        string path,
        ConflictResolution resolution,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await channel.SendAsync(
                new ResolveRequest([path], resolution),
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
