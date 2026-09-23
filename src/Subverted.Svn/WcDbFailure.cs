namespace Subverted.Svn;

/// <summary>
/// Why a working copy could not be read directly. The two cases need opposite responses:
/// <see cref="NotAWorkingCopy"/> is the caller's path being wrong, <see cref="Unreadable"/> is the
/// fast path failing on a working copy that is really there.
/// </summary>
public enum WcDbFailure
{
    /// <summary>No <c>.svn/wc.db</c> at or above the path. <c>svn</c> would refuse it too.</summary>
    NotAWorkingCopy,

    /// <summary>A working copy was found and its metadata could not be read or understood.</summary>
    Unreadable,
}
