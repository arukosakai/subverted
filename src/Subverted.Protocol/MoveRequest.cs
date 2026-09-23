namespace Subverted.Protocol;

/// <summary>
/// Rename a node, keeping its history. What this means is decided by the daemon from what is on
/// disk rather than stated here — a source that is already gone is a rename made in a file manager,
/// and recording that takes different work from making one.
/// </summary>
/// <param name="Source">Absolute path of the node to rename.</param>
/// <param name="Destination">Absolute path it takes, in the same working copy.</param>
public sealed record MoveRequest(string Source, string Destination) : DaemonRequest;
