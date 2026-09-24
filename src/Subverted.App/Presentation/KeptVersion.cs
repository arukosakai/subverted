using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>A resolution as the version it keeps, in the person's words.</summary>
public static class KeptVersion
{
    /// <exception cref="ArgumentOutOfRangeException">Not a member of the enum.</exception>
    public static string Of(ConflictResolution kept) =>
        kept switch
        {
            ConflictResolution.Working => "the files as they are on disk",
            ConflictResolution.Mine => "your version",
            ConflictResolution.Theirs => "the incoming version",
            ConflictResolution.Base => "the revision both sides started from",
            _ => throw new ArgumentOutOfRangeException(nameof(kept)),
        };
}
