using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="Revisions">Newest first, the order SVN lists them in.</param>
public sealed record LogResponse(IReadOnlyList<RevisionEntry> Revisions) : DaemonResponse;
