using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Asks the server what an update of a path would bring down. Declared here for the same reason as
/// <see cref="ReadRevisionLog"/>.
/// </summary>
public delegate Task<IncomingChanges> ReadIncomingChanges(
    string workingCopyRoot,
    string path,
    CancellationToken cancellationToken
);
