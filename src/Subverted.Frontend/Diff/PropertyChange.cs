namespace Subverted.Frontend.Diff;

/// <summary>One property in SVN's <c>Property changes on:</c> section.</summary>
public sealed record PropertyChange(
    string Name,
    PropertyChangeKind Kind,
    IReadOnlyList<Hunk> Hunks
);
