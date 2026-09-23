using Microsoft.Extensions.Hosting;

namespace Subverted.Daemon;

internal sealed class HostDaemonShutdown(IHostApplicationLifetime lifetime) : IDaemonShutdown
{
    public void RequestShutdown() => lifetime.StopApplication();
}
