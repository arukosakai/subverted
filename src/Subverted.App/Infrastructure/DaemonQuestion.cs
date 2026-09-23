using System.Net.Sockets;
using Subverted.App.ViewModels;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>Asks the daemon one thing, turning every way of not reaching it into one exception.</summary>
internal static class DaemonQuestion
{
    /// <exception cref="DaemonUnreachableException">No daemon answered, or it went away mid-answer.</exception>
    public static async Task<DaemonResponse> AskAsync(
        DaemonChannel channel,
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
