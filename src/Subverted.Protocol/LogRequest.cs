using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="Path">Any absolute path inside the working copy; history is read for that path.</param>
/// <param name="Limit">
/// How many revisions, newest first. Null asks for the whole history, which on a studio repository
/// is a long server round trip — the front-end defaults to a cap for that reason.
/// </param>
/// <param name="Start">
/// The newest revision to list. Null keeps <c>svn log</c>'s default for a working-copy path — the
/// path's BASE, so nothing it has not been updated to — which is what <c>sv log</c> shows.
/// </param>
public sealed record LogRequest(string Path, int? Limit, HistoryStart? Start = null)
    : DaemonRequest;
