namespace Subverted.Core;

/// <param name="ResolvedPaths">
/// One entry per node SVN reported resolved, slash-separated, in the order SVN walked them — which
/// is its own tree order, not the order the targets were given. Empty means nothing was conflicted.
/// </param>
/// <param name="Refusals">
/// The nodes SVN would not resolve, in its own words — a tree conflict asked to become anything but
/// <c>working</c>, a path it could not find, a path in no working copy. Unlike a lock these arrive
/// with a non-zero exit as well, but they are still per-path: <c>svn resolve</c> keeps going after
/// one and resolves every other target it was given.
/// </param>
public sealed record ResolveOutcome(
    IReadOnlyList<string> ResolvedPaths,
    IReadOnlyList<string> Refusals
);
