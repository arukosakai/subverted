namespace Subverted.App.ViewModels;

/// <summary>
/// The daemon could not be asked. One type for every way that happens — refused socket, timed-out
/// start, a torn message — because the view says the same thing for all of them.
/// </summary>
public sealed class DaemonUnreachableException(string message, Exception innerException)
    : Exception(message, innerException);
