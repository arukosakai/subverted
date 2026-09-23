namespace Subverted.Frontend.Diff;

/// <summary>One property in SVN's <c>Property changes on:</c> section.</summary>
public sealed record PropertyChange(string Name, PropertyChangeKind Kind, IReadOnlyList<Hunk> Hunks)
{
    /// <summary>
    /// What SVN prints instead of hunks for <c>svn:mergeinfo</c> — lines such as
    /// <c>Merged /branches/a:r3-4</c> and <c>Reverse-merged /branches/b:r3</c>, unindented.
    /// Empty for every other property, whose change is in <see cref="Hunks"/>.
    /// </summary>
    public IReadOnlyList<string> MergeSummary { get; init; } = [];
}
