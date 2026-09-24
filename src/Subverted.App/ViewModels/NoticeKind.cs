namespace Subverted.App.ViewModels;

/// <summary>What a write left behind, which is what decides how its notice is drawn.</summary>
public enum NoticeKind
{
    /// <summary>It did what was asked.</summary>
    Succeeded,

    /// <summary>It wrote to the working copy and then stopped: marks were made, nothing was sent.</summary>
    LeftMarked,

    /// <summary>It was refused before anything was written.</summary>
    NothingWritten,

    /// <summary>It is not known how far it got; the next listing is the answer.</summary>
    Uncertain,

    /// <summary>It finished, and left something only the person can settle: a conflict, a skipped path.</summary>
    NeedsAttention,
}
