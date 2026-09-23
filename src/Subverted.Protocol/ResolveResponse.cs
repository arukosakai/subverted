namespace Subverted.Protocol;

/// <param name="ResolvedPaths">
/// One entry per node resolved, slash-separated. Empty with no refusals means nothing under the
/// targets was conflicted — a front-end that reported that as success would be right, and one that
/// reported it as "resolved" would not.
/// </param>
/// <param name="Refusals">
/// The nodes SVN would not resolve, in its own words. A tree conflict asked to become anything but
/// the working version is the one a person hits.
/// </param>
public sealed record ResolveResponse(
    IReadOnlyList<string> ResolvedPaths,
    IReadOnlyList<string> Refusals
) : DaemonResponse;
