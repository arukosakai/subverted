namespace Subverted.Protocol;

/// <summary>What an update of a path would bring down. Asks the server every time; nothing is held.</summary>
/// <param name="Path">Absolute, inside the working copy; the same scope an update of it would have.</param>
public sealed record IncomingRequest(string Path) : DaemonRequest;
