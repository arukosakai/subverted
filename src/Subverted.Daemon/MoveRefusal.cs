using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Why a rename was not made, in words. Pure, and worth being pure: SVN reports all three of these
/// as <c>E155010: Path '…' is not a directory</c>, so the wording here is the only thing standing
/// between someone and an error message about a directory they never mentioned.
/// </summary>
public static class MoveRefusal
{
    /// <returns>
    /// The explanation, or <see langword="null"/> for a route that did the move — which is how a
    /// caller tells the two apart without listing the routes a second time.
    /// </returns>
    public static string? Explain(MoveRoute route, string source, string destination) =>
        route switch
        {
            MoveRoute.Ordinary or MoveRoute.AlreadyRenamed => null,

            MoveRoute.NothingAtSource =>
                $"There is nothing at '{source}' and nothing at '{destination}', so there is no "
                    + "rename here to make or to record.",

            MoveRoute.DestinationOccupied =>
                $"'{destination}' already exists. Renaming onto it would overwrite it, and SVN "
                    + "has no way to undo that — move or delete it first.",

            MoveRoute.AlreadyRenamedDirectory =>
                $"'{destination}' looks like the directory '{source}' renamed outside SVN. "
                    + "Recording that after the fact means restoring the whole subtree and merging "
                    + "every local change back over it, which Subverted will not do unasked. Rename "
                    + $"it back to '{source}' first, then run the move again.",

            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown move route."),
        };
}
