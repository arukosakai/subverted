namespace Subverted.Protocol;

/// <summary>The steps of a <see cref="CommitSelectionRequest"/>, in the order they run.</summary>
public enum SelectionStep
{
    /// <summary>Recording renames made outside SVN.</summary>
    Move,

    /// <summary>Scheduling unversioned nodes for addition.</summary>
    Addition,

    /// <summary>Recording the deletion of missing nodes.</summary>
    Deletion,

    /// <summary>Sending the lot to the server.</summary>
    Commit,
}
