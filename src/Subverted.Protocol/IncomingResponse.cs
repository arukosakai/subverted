using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="Incoming">As the server answered when asked; stale the moment anyone commits.</param>
public sealed record IncomingResponse(IncomingChanges Incoming) : DaemonResponse;
