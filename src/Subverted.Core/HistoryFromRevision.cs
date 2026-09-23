namespace Subverted.Core;

/// <summary>From a given revision down, which is how a list pages: the next page starts one below
/// the oldest revision already shown.</summary>
/// <param name="Revision">The newest revision to include; at least 1.</param>
public sealed record HistoryFromRevision(long Revision) : HistoryStart;
