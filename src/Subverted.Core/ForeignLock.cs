namespace Subverted.Core;

/// <summary>
/// What a lock operation does about a lock somebody else is holding. The two are different
/// operations rather than a setting: one asks the server for a lock, the other takes one off a
/// teammate who is very probably mid-edit on a file nothing can merge.
/// </summary>
public enum ForeignLock
{
    /// <summary>
    /// Left where it is. SVN refuses the path and names the user holding it, which is the only way
    /// a working copy finds out about somebody else's lock without asking the server directly.
    /// </summary>
    Respected,

    /// <summary>
    /// Taken — <c>svn --force</c>. The holder loses it, and their working copy goes on showing
    /// <c>K</c> until they update, so this is spelled out on the command line every single time.
    /// </summary>
    Overridden,
}
