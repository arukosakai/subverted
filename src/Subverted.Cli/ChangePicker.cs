using Subverted.Core;
using Subverted.Frontend;

namespace Subverted.Cli;

/// <summary>
/// Walks the changed nodes one at a time and accumulates the set to commit. It is handed answers
/// and never reads a console, so the walk is a test rather than something found out halfway
/// through someone's commit. Which nodes a directory's answer settles is
/// <see cref="DecidedSubtrees"/>'s business, shared with every other front-end, apart from the one
/// rule it does not know yet: a missing directory that is sent takes its missing subtree with it.
/// </summary>
public sealed class ChangePicker
{
    private readonly IReadOnlyList<PickCandidate> _candidates;
    private readonly StringComparison _comparison;
    private readonly DecidedSubtrees _decided;
    private readonly List<string> _missingDirectoriesSent = [];
    private readonly List<PickCandidate> _picked = [];
    private readonly List<PickCandidate> _decidedByAnAncestor = [];
    private readonly List<PickCandidate> _newLeftBySendRest = [];
    private int _index;
    private bool _sendRest;
    private bool _finished;

    /// <param name="candidates">
    /// Committable changes, ancestors first — <see cref="PickCandidates.Under"/> is what puts them
    /// in that order, and the directory rules are wrong without it.
    /// </param>
    /// <param name="comparison">How this platform compares paths.</param>
    public ChangePicker(IReadOnlyList<PickCandidate> candidates, StringComparison comparison)
    {
        _candidates = candidates;
        _comparison = comparison;
        _decided = new DecidedSubtrees(comparison);
        Settle();
    }

    /// <summary>The node to ask about, or <see langword="null"/> when there is nothing left to ask.</summary>
    public PickCandidate? Current =>
        _finished || _index >= _candidates.Count ? null : _candidates[_index];

    /// <summary>The walk was stopped with nothing to commit. Distinct from picking nothing.</summary>
    public bool Abandoned { get; private set; }

    /// <summary>What to commit, in the order it was offered.</summary>
    public IReadOnlyList<PickCandidate> Picked => _picked;

    /// <summary>
    /// Nodes never offered because a directory above them had already settled them — sent with it,
    /// left with it, or unable to go without it. Reported so the count on screen adds up.
    /// </summary>
    public IReadOnlyList<PickCandidate> DecidedByAnAncestor => _decidedByAnAncestor;

    /// <summary>
    /// Unversioned nodes <see cref="PickAnswer.SendRest"/> passed over. It never adds what nobody
    /// answered for, because what sits unversioned in a tree is as often build output as new work.
    /// </summary>
    public IReadOnlyList<PickCandidate> NewLeftBySendRest => _newLeftBySendRest;

    /// <exception cref="InvalidOperationException">There is no node to answer for.</exception>
    public void Answer(PickAnswer answer)
    {
        if (Current is not { } candidate)
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
                Take(candidate);
                break;

            case PickAnswer.Send:
                Take(candidate);
                break;

            case PickAnswer.Skip:
                Leave(candidate);
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
    /// every remaining node that an ancestor did not rule out, apart from unversioned ones.
    /// </summary>
    private void Settle()
    {
        while (_index < _candidates.Count)
        {
            var candidate = _candidates[_index];
            if (IsDecided(candidate))
            {
                _decidedByAnAncestor.Add(candidate);
            }
            else if (_sendRest && candidate.IsNew)
            {
                _newLeftBySendRest.Add(candidate);
            }
            else if (_sendRest)
            {
                Take(candidate);
            }
            else
            {
                return;
            }

            _index++;
        }
    }

    private bool IsDecided(PickCandidate candidate) =>
        Halves(candidate)
            .Any(entry => _decided.Decides(entry) || IsBelowASentMissingDirectory(entry));

    /// <summary>
    /// Measured on 1.8.15: <c>svn delete</c> on a missing directory records the whole subtree, so
    /// the children follow a yes. After a no each child can still be deleted and committed alone.
    /// </summary>
    private bool IsBelowASentMissingDirectory(WorkingCopyEntry entry) =>
        entry.Status == NodeStatus.Missing
        && _missingDirectoriesSent.Any(directory =>
            TargetCoverage.Below(directory, entry.RelPath, _comparison)
        );

    private void Take(PickCandidate candidate)
    {
        _picked.Add(candidate);
        foreach (var entry in Halves(candidate))
        {
            _decided.Sent(entry);
            if (entry is { Kind: NodeKind.Directory, Status: NodeStatus.Missing })
            {
                _missingDirectoriesSent.Add(entry.RelPath);
            }
        }
    }

    private void Leave(PickCandidate candidate)
    {
        foreach (var entry in Halves(candidate))
        {
            _decided.Left(entry);
        }
    }

    private static IEnumerable<WorkingCopyEntry> Halves(PickCandidate candidate) =>
        candidate.RenamedFrom is { } from ? [from, candidate.Entry] : [candidate.Entry];
}
