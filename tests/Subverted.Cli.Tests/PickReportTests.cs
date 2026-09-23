using Subverted.Core;

namespace Subverted.Cli.Tests;

/// <summary>
/// The text somebody answers nine times in a row, and the list they read before it all goes to the
/// server. Both are worth pinning: the question has to name the node it is about, and the list has
/// to account for every node that moves.
/// </summary>
public sealed class PickReportTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    [Test]
    public async Task The_question_shows_the_node_the_way_sv_st_shows_it()
    {
        var question = PickReport.Question(Modified("src/a.txt"), StatusPalette.Plain);

        await Assert.That(question).IsEqualTo("M       src/a.txt  send? [y,n,a,d,q,?] ");
    }

    /// <summary>
    /// The answer is typed on the same line as the question, so a trailing newline would leave the
    /// cursor under it and the prompt reading like a heading.
    /// </summary>
    [Test]
    public async Task The_question_does_not_end_the_line()
    {
        var question = PickReport.Question(Modified("src/a.txt"), StatusPalette.Plain);

        await Assert.That(question).DoesNotContain("\n");
        await Assert.That(question.EndsWith(' ')).IsTrue();
    }

    [Test]
    public async Task The_question_takes_the_colour_it_was_given()
    {
        var question = PickReport.Question(Modified("src/a.txt"), StatusPalette.Ansi);

        await Assert.That(question).StartsWith("\e[33m");
    }

    [Test]
    public async Task Every_letter_the_prompt_offers_is_explained()
    {
        foreach (var letter in new[] { "y", "n", "a", "d", "q", "?" })
        {
            await Assert
                .That(PickReport.Explanation.Any(line => line.StartsWith($"  {letter}  ")))
                .IsTrue();
        }
    }

    [Test]
    public async Task The_opening_line_says_how_many_nodes_there_are_to_go_through()
    {
        await Assert.That(PickReport.Opening(9)).StartsWith("9 change(s)");
    }

    [Test]
    public async Task What_is_about_to_be_sent_is_listed_with_its_count()
    {
        var picker = new ChangePicker([Modified("src/a.txt"), Modified("src/b.txt")], Sensitive);
        picker.Answer(PickAnswer.Send);
        picker.Answer(PickAnswer.Skip);

        var lines = PickReport.Sending(picker, StatusPalette.Plain);

        await Assert.That(lines).IsEquivalentTo(["sending 1 node(s):", "M       src/a.txt"]);
    }

    /// <summary>
    /// A directory's answer settles everything under it, and those nodes were never asked about.
    /// Without this line the number sent reads as wrong against what <c>sv st</c> had shown.
    /// </summary>
    [Test]
    public async Task Nodes_a_directory_settled_are_accounted_for_rather_than_left_unexplained()
    {
        var picker = new ChangePicker(
            [Directory("art", NodeStatus.Deleted), Deleted("art/hero.png")],
            Sensitive
        );
        picker.Answer(PickAnswer.Send);

        var lines = PickReport.Sending(picker, StatusPalette.Plain);

        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That(lines[2]).Contains("1 node(s) below a directory you answered for");
    }

    [Test]
    public async Task Nothing_was_settled_by_a_directory_means_no_line_about_it()
    {
        var picker = new ChangePicker([Modified("src/a.txt")], Sensitive);
        picker.Answer(PickAnswer.Send);

        var lines = PickReport.Sending(picker, StatusPalette.Plain);

        await Assert.That(lines.Count).IsEqualTo(2);
    }

    private static WorkingCopyEntry Modified(string relPath) =>
        new(
            relPath,
            NodeKind.File,
            NodeStatus.Modified,
            PropertyStatus.Unmodified,
            Revision: 1,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );

    private static WorkingCopyEntry Deleted(string relPath) =>
        Modified(relPath) with
        {
            Status = NodeStatus.Deleted,
        };

    private static WorkingCopyEntry Directory(string relPath, NodeStatus status) =>
        Modified(relPath) with
        {
            Kind = NodeKind.Directory,
            Status = status,
        };
}
