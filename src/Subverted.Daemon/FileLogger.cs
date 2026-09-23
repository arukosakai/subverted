using Microsoft.Extensions.Logging;

namespace Subverted.Daemon;

internal sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        var detail = exception is null ? string.Empty : $" {exception}";
        provider.Append($"{DateTimeOffset.UtcNow:O} {logLevel, -11} {category} {message}{detail}");
    }
}
