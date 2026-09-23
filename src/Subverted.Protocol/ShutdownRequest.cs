namespace Subverted.Protocol;

/// <summary>Asks the daemon to exit once it has answered.</summary>
public sealed record ShutdownRequest : DaemonRequest;
