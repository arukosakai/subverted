using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="Paths">
/// Absolute paths, all inside one working copy. Directories are allowed and usual — the daemon
/// always recurses. The daemon resolves the first to a root and rejects the request if any of the
/// others fall outside it.
/// </param>
/// <param name="Resolution">
/// Which version to keep. Required: there is no safe default, and the version SVN would ask about
/// interactively cannot be asked for from here.
/// </param>
public sealed record ResolveRequest(IReadOnlyList<string> Paths, ConflictResolution Resolution)
    : DaemonRequest;
