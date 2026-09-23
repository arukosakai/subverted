using Microsoft.Extensions.Logging;

namespace Subverted.Daemon;

/// <summary>
/// Appends the daemon's log to one file next to its socket. A daemon started in the background has
/// nowhere else to put it, and a daemon with no log is one nobody can diagnose after the fact.
/// </summary>
public sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    private readonly Lock _gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Append(string line)
    {
        // Serialised rather than buffered: the interesting entries are the ones written just
        // before something goes wrong, and a buffer is exactly what loses those.
        lock (_gate)
        {
            try
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // A daemon that cannot write its log still has a job to do.
            }
        }
    }

    public void Dispose() { }
}
