using Subverted.Core;

namespace Subverted.Cli;

/// <summary>
/// The picker's own text: what it asks, what the letters mean, and what it is about to send. Pure,
/// so the wording of the question somebody answers nine times in a row is a test.
/// </summary>
public static class PickReport
{
    /// <summary>The question, without a trailing newline — the answer is typed on the same line.</summary>
    public static string Question(PickCandidate candidate, Paint paint) =>
        $"{Describe(candidate, paint)}  send? [y,n,a,d,q,?] ";

    /// <summary>
    /// The node the way <c>sv st</c> shows it, and — when sending it marks something first — what
    /// that mark is, since saying yes now does what <c>sv add</c>, <c>sv rm</c> or <c>sv mv</c> did.
    /// </summary>
    public static string Describe(PickCandidate candidate, Paint paint)
    {
        var entry = candidate.Entry;
        if (candidate.RenamedFrom is { } from)
        {
            return paint($"{StatusLine.Compact(from)} -> {entry.RelPath}", from)
                + "  (renamed outside SVN — sent as a move, history kept)";
        }

        var line = paint(StatusLine.Compact(entry), entry);
        return entry switch
        {
            { Status: NodeStatus.Unversioned, Kind: NodeKind.Directory } => line
                + "  (new — sending adds it and everything in it that is not ignored)",
            { Status: NodeStatus.Unversioned } => line + "  (new — sending adds it)",
            { Status: NodeStatus.Missing, Kind: NodeKind.Directory } => line
                + "  (gone from disk — sending records it and everything under it as deleted)",
            { Status: NodeStatus.Missing } => line
                + "  (gone from disk — sending records the deletion)",
            _ => line,
        };
    }

    /// <summary>What each letter does, for <c>?</c> and for an answer that was not one.</summary>
    public static IReadOnlyList<string> Explanation { get; } =
    [
        "  y  send this node",
        "  n  leave it local",
        "  a  send this one and every one left, without asking again — except new (?) nodes,",
        "     which are only ever added when answered y",
        "  d  stop asking and commit what has been picked",
        "  q  stop and commit nothing",
        "  ?  this",
    ];

    /// <param name="count">How many nodes the walk is about to go through.</param>
    public static string Opening(int count) =>
        $"{count} change(s) to go through — y to send, n to leave it, ? for the rest.";

    /// <summary>What is about to be committed, as the lines to print before sending it.</summary>
    public static IReadOnlyList<string> Sending(ChangePicker picker, Paint paint) =>
        [
            $"sending {picker.Picked.Count} node(s):",
            .. picker.Picked.Select(candidate => Describe(candidate, paint)),
            .. Carried(picker),
            .. NotAdded(picker),
        ];

    /// <summary>
    /// A directory's answer settles everything under it, so those nodes were never asked about.
    /// Saying nothing would leave the number sent looking wrong against what <c>sv st</c> showed.
    /// </summary>
    private static IReadOnlyList<string> Carried(ChangePicker picker) =>
        picker.DecidedByAnAncestor.Count == 0
            ? []
            :
            [
                $"({picker.DecidedByAnAncestor.Count} node(s) below a directory you answered for "
                    + "follow that answer; SVN does not commit them separately)",
            ];

    /// <summary><c>a</c> passes over new nodes, and says how many so they are not a surprise later.</summary>
    private static IReadOnlyList<string> NotAdded(ChangePicker picker) =>
        picker.NewLeftBySendRest.Count == 0
            ? []
            :
            [
                $"({picker.NewLeftBySendRest.Count} new node(s) left unversioned — `a` does not "
                    + "add; answer y to each one you want added)",
            ];
}
