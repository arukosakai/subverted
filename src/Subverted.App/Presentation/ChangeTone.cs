namespace Subverted.App.Presentation;

/// <summary>
/// The colour family a change is drawn in. Fewer tones than there are statuses on purpose: an
/// artist scanning the list needs "will this commit", "is this broken" and "is this mine", not
/// SVN's full vocabulary.
/// </summary>
public enum ChangeTone
{
    Modified,
    Added,
    Deleted,
    Replaced,
    Conflict,
    Missing,
    Unversioned,
    Quiet,
}
