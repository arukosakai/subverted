namespace Subverted.Core;

/// <summary>What one revision did to one path — the letter <c>svn log -v</c> prints beside it.</summary>
public enum PathChange
{
    Added,
    Deleted,
    Modified,

    /// <summary>Deleted and added again in the same revision, which SVN reports as one change.</summary>
    Replaced,
}
