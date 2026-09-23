namespace Subverted.Daemon;

/// <summary>
/// Stops the daemon. Named for what the request handler needs rather than for the Generic Host
/// that happens to provide it.
/// </summary>
public interface IDaemonShutdown
{
    void RequestShutdown();
}
