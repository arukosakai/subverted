namespace Subverted.Protocol;

/// <param name="Path">Any absolute path inside the working copy; the diff covers it and below.</param>
public sealed record DiffRequest(string Path) : DaemonRequest;
