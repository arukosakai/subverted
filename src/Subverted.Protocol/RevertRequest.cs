namespace Subverted.Protocol;

/// <summary>
/// Throw away local changes under these paths. Irreversible, and the daemon does not ask: whatever
/// sends this has already confirmed it with the person whose work it is.
/// </summary>
/// <param name="Paths">Absolute paths, all inside one working copy, reverted to infinite depth.</param>
public sealed record RevertRequest(IReadOnlyList<string> Paths) : DaemonRequest;
