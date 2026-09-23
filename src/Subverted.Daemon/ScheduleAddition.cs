namespace Subverted.Daemon;

/// <summary>
/// Schedules paths for addition and reports what the client said. Declared here rather than taken
/// as an SVN type so the request handler can be tested without <c>svn</c> on PATH.
/// </summary>
public delegate Task<string> ScheduleAddition(
    string workingCopyRoot,
    IReadOnlyList<string> paths,
    CancellationToken cancellationToken
);
