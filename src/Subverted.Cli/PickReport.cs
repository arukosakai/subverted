using Subverted.Core;

namespace Subverted.Cli;

/// <summary>
/// The picker's own text: what it asks, what the letters mean, and what it is about to send. Pure,
/// so the wording of the question somebody answers nine times in a row is a test.
/// </summary>
public static class PickReport
{
    /// <summary>The question, without a trailing newline — the answer is typed on the same line.</summary>
    public static string Question(WorkingCopyEntry entry, Paint paint) =>
        $"{paint(StatusLine.Compact(entry), entry)}  send? [y,n,a,d,q,?] ";

    /// <summary>What each letter does, for <c>?</c> and for an answer that was not one.</summary>
    public static IReadOnlyList<string> Explanation { get; } =
    [
        "  y  send this node",
        "  n  leave it local",
        "  a  send this one and every one left, without asking again",
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
            .. picker.Picked.Select(entry => paint(StatusLine.Compact(entry), entry)),
            .. Carried(picker),
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
}
