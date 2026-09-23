namespace Subverted.Core;

/// <summary>
/// Which version of a conflicted node the working copy keeps. There is no default member on
/// purpose: every one of these picks somebody's work over somebody else's, and the one SVN would
/// ask about interactively is the one a daemon has no terminal to ask in.
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// The file exactly as it sits on disk, marked resolved and not otherwise touched. What you use
    /// after editing the merge by hand — and the one that will happily mark a file still full of
    /// <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> as finished, because SVN does not read the file to check.
    /// </summary>
    Working,

    /// <summary>
    /// This working copy's version, whole — <c>--accept mine-full</c>. The incoming change is
    /// dropped, and being in the repository is what makes that recoverable.
    /// </summary>
    Mine,

    /// <summary>
    /// The incoming version, whole — <c>--accept theirs-full</c>. Discards what this working copy
    /// had, which for an unshared edit is the copy that existed.
    /// </summary>
    Theirs,

    /// <summary>
    /// The revision both sides started from — <c>--accept base</c>. Discards both edits.
    /// </summary>
    Base,
}
