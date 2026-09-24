namespace Subverted.App.Presentation;

/// <summary>Which nodes the Changes table lists: the toggle beside its filter.</summary>
public enum ListedNodes
{
    /// <summary>What <c>svn status</c> prints: changes, and clean files that hold a lock.</summary>
    Changes,

    /// <summary>
    /// Every versioned file as well, as <c>svn status -v</c> would — so a file nobody has touched
    /// yet has a line to lock from.
    /// </summary>
    All,
}
