using Subverted.Core;

namespace Subverted.Frontend;

/// <summary>
/// Which resolutions throw away what is in the working copy, which is what decides between asking
/// first and simply running — for every front-end, so they cannot disagree about it.
/// </summary>
public static class ResolutionRisk
{
    /// <remarks>
    /// <see cref="ConflictResolution.Mine"/> rewrites the file too — it drops the merge markers —
    /// but everything it discards is the incoming revision, which is still in the repository. These
    /// two discard the local side, and for an edit nobody has committed there is no second copy.
    /// </remarks>
    public static bool OverwritesLocalWork(ConflictResolution resolution) =>
        resolution is ConflictResolution.Theirs or ConflictResolution.Base;
}
