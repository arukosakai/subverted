namespace Subverted.Protocol;

/// <summary>
/// Why a request could not be answered. Front-ends map these to exit codes; the accompanying
/// message is what gets printed.
/// </summary>
public enum DaemonErrorKind
{
    /// <summary>The path exists but no working copy contains it.</summary>
    NotAWorkingCopy,

    /// <summary>A working copy was found and could not be read — both the fast path and its fallback.</summary>
    WorkingCopyUnreadable,

    /// <summary>
    /// The <c>svn</c> client could not answer — not on PATH, or the server refused. Retrying
    /// through Subverted will not help; the same command typed by hand is the thing to try.
    /// </summary>
    SvnCommandFailed,

    /// <summary>
    /// The command line was well formed and the working copy disagrees with it — a rename whose
    /// destination is already taken, or whose source is not there. Nothing was touched, and the
    /// message says which of them it was.
    /// </summary>
    RequestRefused,

    /// <summary>A bug in the daemon. The message is a diagnostic, not advice.</summary>
    Internal,
}
