namespace Subverted.Frontend.Diff;

/// <summary>What <c>svn diff</c> said, one entry per file it printed, in its order.</summary>
public sealed record DiffDocument(IReadOnlyList<FileDiff> Files)
{
    public static DiffDocument Empty { get; } = new([]);
}
