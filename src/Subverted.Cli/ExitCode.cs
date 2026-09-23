namespace Subverted.Cli;

/// <summary>
/// What <c>sv</c> returns. The split that matters is user error from daemon failure: a script
/// that sees <see cref="DaemonFailure"/> should retry or fall back to <c>svn</c>, and one that
/// sees <see cref="UserError"/> should not.
/// </summary>
public static class ExitCode
{
    public const int Success = 0;

    /// <summary>A bad command line, or a path that is not in a working copy.</summary>
    public const int UserError = 1;

    /// <summary>The daemon could not be reached, started, or understood.</summary>
    public const int DaemonFailure = 2;

    /// <summary>
    /// The <c>svn</c> client could not answer. Distinct from <see cref="DaemonFailure"/> because
    /// the advice is the opposite: falling back to <c>svn</c> is exactly what already failed.
    /// </summary>
    public const int SvnFailure = 3;

    /// <summary>
    /// The command ran and left something for a human — a conflicted or skipped path after an
    /// update, a lock that was refused, or a conflict that would not resolve.
    /// </summary>
    /// <remarks>
    /// A deliberate divergence from <c>svn</c>, which exits zero for all three. That exit code says
    /// the client ran, not that the work was done: <c>svn up &amp;&amp; build</c> is how a file full
    /// of conflict markers gets compiled, and <c>svn lock</c> exiting zero is how two artists end up
    /// editing one asset.
    /// </remarks>
    public const int NeedsAttention = 4;
}
