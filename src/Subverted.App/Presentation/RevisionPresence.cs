namespace Subverted.App.Presentation;

/// <summary>Whether a revision has reached this working copy, judged against its BASE range.</summary>
public enum RevisionPresence
{
    /// <summary>At or below every node's BASE: the copy has it everywhere.</summary>
    InCopy,

    /// <summary>
    /// Above the lowest BASE but not the highest — a mixed copy has it in some folders and not in
    /// others.
    /// </summary>
    PartlyInCopy,

    /// <summary>Newer than every node's BASE: an update would bring it in.</summary>
    NotInCopy,
}
