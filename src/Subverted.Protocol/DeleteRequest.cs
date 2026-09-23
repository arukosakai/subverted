namespace Subverted.Protocol;

/// <summary>
/// Schedule these paths for deletion and remove them from disk. Irreversible for anything not
/// already committed, and the daemon does not ask: whatever sends this has confirmed it.
/// </summary>
/// <remarks>
/// An unversioned path among them is the dangerous one — SVN has no pristine to restore it from, so
/// it is simply unlinked. A front-end that does not say so before sending this is not doing its job.
/// </remarks>
/// <param name="Paths">Absolute paths, all inside one working copy.</param>
public sealed record DeleteRequest(IReadOnlyList<string> Paths) : DaemonRequest;
