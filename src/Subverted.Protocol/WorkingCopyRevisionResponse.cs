using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="Range">
/// Null when nothing there has a BASE — a path that is only scheduled for addition. Local, read
/// from the working copy: no server is asked, so it says nothing about what HEAD is.
/// </param>
public sealed record WorkingCopyRevisionResponse(BaseRevisionRange? Range) : DaemonResponse;
