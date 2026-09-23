using Subverted.Core;

namespace Subverted.Frontend;

/// <summary>
/// The nodes a directory's answer has already settled, so a front-end never offers them as a
/// choice they are not. One copy of D20's rules, for every front-end that lets someone pick what a
/// <see cref="CommitScope.ExactlyTheseNodes"/> commit sends.
/// </summary>
/// <remarks>
/// Both rules were read off <c>svn</c> 1.8.15. A child whose added or replaced parent is not in the
/// same commit is refused outright (E200009), so leaving such a directory out rules out everything
/// below it. A deletion below a deleted or replaced directory is recorded on that directory, so it
/// travels with the directory whichever way the directory was answered.
/// </remarks>
/// <param name="comparison">How this platform compares paths.</param>
public sealed class DecidedSubtrees(StringComparison comparison)
{
    private readonly List<string> _unavailableUnder = [];
    private readonly List<string> _carriedUnder = [];

    /// <summary>Records that <paramref name="entry"/> goes in the commit. Answer ancestors first.</summary>
    public void Sent(WorkingCopyEntry entry) => RecordWhatItCarries(entry);

    /// <summary>Records that <paramref name="entry"/> stays local. Answer ancestors first.</summary>
    public void Left(WorkingCopyEntry entry)
    {
        RecordWhatItCarries(entry);
        if (
            entry.Kind == NodeKind.Directory
            && entry.Status is NodeStatus.Added or NodeStatus.Replaced
        )
        {
            _unavailableUnder.Add(entry.RelPath);
        }
    }

    /// <returns>
    /// True when a directory answered for above <paramref name="entry"/> has already decided it —
    /// sent with it, left with it, or unable to go without it — so it is not a choice.
    /// </returns>
    public bool Decides(WorkingCopyEntry entry) =>
        _unavailableUnder.Any(ancestor => Below(ancestor, entry))
        || (
            entry.Status == NodeStatus.Deleted
            && _carriedUnder.Any(ancestor => Below(ancestor, entry))
        );

    private void RecordWhatItCarries(WorkingCopyEntry entry)
    {
        if (
            entry.Kind == NodeKind.Directory
            && entry.Status is NodeStatus.Deleted or NodeStatus.Replaced
        )
        {
            _carriedUnder.Add(entry.RelPath);
        }
    }

    private bool Below(string ancestor, WorkingCopyEntry entry) =>
        TargetCoverage.Below(ancestor, entry.RelPath, comparison);
}
