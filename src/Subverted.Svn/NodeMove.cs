using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Renames a node inside a working copy, whichever state the filesystem is already in — the rename
/// still to make, and the one already made in a file manager.
/// </summary>
public sealed class NodeMove(SvnMoveCommand move, UnrecordedMoveRepair repair)
{
    /// <param name="source">Absolute path of the node to rename.</param>
    /// <param name="destination">Absolute path it takes, in the same working copy.</param>
    /// <returns>
    /// What was done and what SVN said about it. A <see cref="MoveOutcome.Route"/> that refuses
    /// carries no notifications and means nothing was touched — the caller explains it, because the
    /// wording belongs to whoever is talking to the person.
    /// </returns>
    /// <exception cref="SvnCommandException">The client failed, or a repair could not be unwound.</exception>
    public async Task<MoveOutcome> MoveAsync(
        string workingCopyRoot,
        string source,
        string destination,
        CancellationToken cancellationToken
    )
    {
        var route = MoveRouteResolver.Resolve(Path.Exists(source), KindAt(destination));
        return route switch
        {
            MoveRoute.Ordinary => new MoveOutcome(
                route,
                await move.MoveAsync(workingCopyRoot, source, destination, cancellationToken)
            ),
            MoveRoute.AlreadyRenamed => new MoveOutcome(
                route,
                await repair.RepairAsync(workingCopyRoot, source, destination, cancellationToken)
            ),
            _ => new MoveOutcome(route, string.Empty),
        };
    }

    /// <returns>
    /// <see cref="NodeKind.Directory"/>, <see cref="NodeKind.File"/>, or <see langword="null"/> for
    /// a path that holds nothing. Directory first: on Windows a directory answers
    /// <see cref="File.Exists"/> with false, but asking in this order needs no platform rule.
    /// </returns>
    private static NodeKind? KindAt(string path) =>
        Directory.Exists(path) ? NodeKind.Directory
        : File.Exists(path) ? NodeKind.File
        : null;
}
