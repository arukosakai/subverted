using System.Net.Sockets;
using Subverted.App.ViewModels;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>Asks the daemon what a delete would reach and then to delete it, starting it from beside the app if needed.</summary>
public sealed class DaemonWorkingCopyDeletion(DaemonChannel channel) : IWorkingCopyDeletion
{
    /// <remarks>
    /// Unmodified and ignored nodes are both asked for: a clean file is the commonest thing deleted,
    /// and an ignored one is unlinked with no pristine behind it and no line from SVN about it.
    /// </remarks>
    public Task<DaemonResponse> ListAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(
            new StatusRequest(path, IncludeUnmodified: true, IncludeIgnored: true, Scope: [path]),
            cancellationToken
        );

    public Task<DaemonResponse> DeleteAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(new DeleteRequest([path]), cancellationToken);

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
