using System.Net.Sockets;
using Subverted.App.ViewModels;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>
/// Asks the daemon to lock or unlock one file, starting it from beside the app if needed. Neither
/// takes a lock off somebody else: stealing and breaking are not offered here.
/// </summary>
public sealed class DaemonWorkingCopyLocks(DaemonChannel channel) : IWorkingCopyLocks
{
    public Task<DaemonResponse> LockAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(new LockRequest([path], Comment: null), cancellationToken);

    public Task<DaemonResponse> UnlockAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(new UnlockRequest([path]), cancellationToken);

    private async Task<DaemonResponse> SendAsync(
        DaemonRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await channel.SendAsync(request, cancellationToken);
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
