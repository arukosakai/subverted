using Subverted.Core;

namespace Subverted.Cli;

/// <summary>
/// Walks the changed nodes one at a time and accumulates the set to commit. It is handed answers
/// and never reads a console, so the two rules SVN imposes on directories are tests rather than
/// something found out halfway through someone's commit.
/// </summary>
/// <remarks>
/// Both rules were taken from <c>svn</c> 1.8.15 rather than from the documentation. A child whose
/// added or replaced parent is not in the same commit is refused outright (E200009), and a deletion
/// below a deleted or replaced directory travels with that directory whichever way it was answered.
/// Either way the node is not a choice, so it is not offered as one.
/// </remarks>
public sealed class ChangePicker
{
    private readonly IReadOnlyList<WorkingCopyEntry> _candidates;
    private readonly StringComparison _comparison;
    private readonly List<WorkingCopyEntry> _picked = [];
    private readonly List<WorkingCopyEntry> _decidedByAnAncestor = [];
    private readonly List<string> _unavailableUnder = [];
    private readonly List<string> _carriedUnder = [];
    private int _index;
    private bool _sendRest;
    private bool _finished;

    /// <param name="candidates">
    /// Committable changes, ancestors first — <see cref="PickCandidates.Under"/> is what puts them
    /// in that order, and the directory rules are wrong without it.
    /// </param>
    /// <param name="comparison">How this platform compares paths.</param>
    public ChangePicker(IReadOnlyList<WorkingCopyEntry> candidates, StringComparison comparison)
    {
        _candidates = candidates;
        _comparison = comparison;
        Settle();
    }

    /// <summary>The node to ask about, or <see langword="null"/> when there is nothing left to ask.</summary>
    public WorkingCopyEntry? Current =>
        _finished || _index >= _candidates.Count ? null : _candidates[_index];

    /// <summary>The walk was stopped with nothing to commit. Distinct from picking nothing.</summary>
    public bool Abandoned { get; private set; }

    /// <summary>What to commit, in the order it was offered.</summary>
    public IReadOnlyList<WorkingCopyEntry> Picked => _picked;

    /// <summary>
    /// Nodes never offered because a directory above them had already settled them — sent with it,
    /// left with it, or unable to go without it. Reported so the count on screen adds up.
    /// </summary>
    public IReadOnlyList<WorkingCopyEntry> DecidedByAnAncestor => _decidedByAnAncestor;

    /// <exception cref="InvalidOperationException">There is no node to answer for.</exception>
    public void Answer(PickAnswer answer)
    {
        if (Current is not { } entry)
        {
            throw new InvalidOperationException(
                "There is nothing left to pick, so there is nothing this answers."
            );
        }

        switch (answer)
        {
            case PickAnswer.Explain:
                return;

            case PickAnswer.Quit:
                Abandoned = true;
                _picked.Clear();
                _finished = true;
                return;

            case PickAnswer.SkipRest:
                _finished = true;
                return;

            case PickAnswer.SendRest:
                _sendRest = true;
                Take(entry);
                break;

            case PickAnswer.Send:
                Take(entry);
                break;

            case PickAnswer.Skip:
                Leave(entry);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(answer),
                    answer,
                    "This is not an answer the picker knows, and guessing at one commits a file."
                );
        }

        _index++;
        Settle();
    }

    /// <summary>
    /// Moves to the next node that is still a choice, picking up everything that is not on the way:
    /// nodes an ancestor already settled, and — once <see cref="PickAnswer.SendRest"/> was given —
    /// every remaining node that an ancestor did not rule out.
    /// </summary>
    private void Settle()
    {
        while (_index < _candidates.Count)
        {
            var entry = _candidates[_index];
            if (IsDecidedByAnAncestor(entry))
            {
                _decidedByAnAncestor.Add(entry);
            }
            else if (_sendRest)
            {
                Take(entry);
            }
            else
            {
                return;
            }

            _index++;
        }
    }

    private void Take(WorkingCopyEntry entry)
    {
        _picked.Add(entry);
        RecordWhatItCarries(entry);
    }

    private void Leave(WorkingCopyEntry entry)
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

    private bool IsDecidedByAnAncestor(WorkingCopyEntry entry) =>
        _unavailableUnder.Any(ancestor => Below(ancestor, entry))
        || (
            entry.Status == NodeStatus.Deleted
            && _carriedUnder.Any(ancestor => Below(ancestor, entry))
        );

    private bool Below(string ancestor, WorkingCopyEntry entry) =>
        TargetCoverage.Below(ancestor, entry.RelPath, _comparison);
}
