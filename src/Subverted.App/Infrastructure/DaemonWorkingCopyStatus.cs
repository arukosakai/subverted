using System.Net.Sockets;
using Subverted.App.ViewModels;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.Infrastructure;

/// <summary>
/// Asks the daemon, starting it from beside the app if nothing answers. Scoped to the folder that
/// was opened, so opening a subfolder of a working copy shows that subfolder, as
/// <c>svn status PATH</c> would.
/// </summary>
public sealed class DaemonWorkingCopyStatus(DaemonChannel channel) : IWorkingCopyStatus
{
    public async Task<DaemonResponse> ReadAsync(
        string workingCopyPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await channel.SendAsync(
                new StatusRequest(
                    workingCopyPath,
                    IncludeUnmodified: false,
                    IncludeIgnored: false,
                    Scope: [workingCopyPath]
                ),
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
