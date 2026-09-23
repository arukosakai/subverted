namespace Subverted.Core;

/// <summary>
/// From the newest revision the repository has, which includes revisions this working copy has not
/// been updated to — the ones <c>svn log</c> on a working-copy path leaves out by default.
/// </summary>
public sealed record HistoryFromHead : HistoryStart;
