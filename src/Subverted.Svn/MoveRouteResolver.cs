using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Decides what a rename means from what is on disk. Pure and I/O-free, because every one of these
/// answers is a case SVN itself reports as the same unhelpful <c>is not a directory</c>, and being
/// able to tell them apart is the whole point.
/// </summary>
internal static class MoveRouteResolver
{
    /// <param name="sourceExists">Whether anything is at the source path — file or directory.</param>
    /// <param name="destinationKind">
    /// What is at the destination, or <see langword="null"/> when it is free. Only a directory is
    /// singled out: a symlink is a file as far as a rename goes, and is treated as one.
    /// </param>
    public static MoveRoute Resolve(bool sourceExists, NodeKind? destinationKind) =>
        (sourceExists, destinationKind) switch
        {
            (true, null) => MoveRoute.Ordinary,
            (true, not null) => MoveRoute.DestinationOccupied,
            (false, NodeKind.Directory) => MoveRoute.AlreadyRenamedDirectory,
            (false, not null) => MoveRoute.AlreadyRenamed,
            (false, null) => MoveRoute.NothingAtSource,
        };
}
