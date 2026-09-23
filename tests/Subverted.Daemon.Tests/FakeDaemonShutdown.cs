namespace Subverted.Daemon.Tests;

internal sealed class FakeDaemonShutdown : IDaemonShutdown
{
    public int Requests { get; private set; }

    public void RequestShutdown() => Requests++;
}
