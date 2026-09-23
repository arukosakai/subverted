using System.Net.Sockets;
using Subverted.App.ViewModels;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>Asks the daemon to revert a confirmed path, starting it from beside the app if needed.</summary>
public sealed class DaemonWorkingCopyRevert(DaemonChannel channel) : IWorkingCopyRevert
{
    public async Task<DaemonResponse> RevertAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await channel.SendAsync(new RevertRequest([path]), cancellationToken);
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
