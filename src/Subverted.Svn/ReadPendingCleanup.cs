namespace Subverted.Svn;

/// <summary>
/// Reads what a working copy is stuck on. Taken as a dependency rather than called directly so a
/// cleanup can be tested without a wc.db on disk — <c>svn cleanup</c>'s own output says nothing, so
/// this read is the entire result and faking it is the only way to test the reporting.
/// </summary>
public delegate PendingCleanup ReadPendingCleanup(string workingCopyRoot);
