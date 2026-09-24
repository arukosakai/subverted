using Subverted.Core;
using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// The second between reading what a removal takes and sending it, driven without a terminal.
/// A <see cref="FakePrompt"/> with no answers fails the test if anything is asked.
/// </summary>
public sealed class RemovalConversationTests
{
    private static readonly string Root = Path.GetFullPath("/wc");

    private static readonly DeletionLine Clean = new("art/hero.png", "Deleted from disk", false);

    private static readonly RemovalPreview OneClean = new(
        [],
        [new RemovedNode(Entry("art/hero.png"), Clean)],
        []
    );

    /// <summary>
    /// The bug this closes: <c>--yes</c> sent an obstruction, and the delete stopped part-way in a
    /// state neither <c>svn status</c> nor <c>svn cleanup</c> could read.
    /// </summary>
    [Test]
    public async Task A_refused_target_is_never_sent_even_with_yes()
    {
        var talk = new Talk();
        var preview = Preview(Entry("art/hero.png") with { Status = NodeStatus.Obstructed });

        var instead = talk.Conversation.Confirm(
            preview,
            alreadyConfirmed: true,
            inputRedirected: false
        );

        await Assert.That(instead).IsEqualTo(ExitCode.UserError);
        await Assert
            .That(talk.Errors.ToString().Split(Environment.NewLine))
            .IsEquivalentTo([
                "sv: Something else is in art/hero.png's place on disk. Deleting it makes SVN stop "
                    + "part-way and leaves a cleanup that fails too; move what is there out of the way first.",
                "sv: nothing removed",
                string.Empty,
            ]);
        await Assert.That(talk.Shown).IsEmpty();
    }

    [Test]
    public async Task A_refused_target_is_refused_before_anything_is_shown_or_asked()
    {
        var talk = new Talk();
        var preview = OneClean with { Refusals = ["art/x is in conflict."] };

        var instead = talk.Conversation.Confirm(
            preview,
            alreadyConfirmed: false,
            inputRedirected: false
        );

        await Assert.That(instead).IsEqualTo(ExitCode.UserError);
        await Assert.That(talk.Shown).IsEmpty();
        await Assert.That(talk.Prompt.Written).IsEmpty();
    }

    [Test]
    public async Task Nothing_reached_is_nothing_to_remove_and_not_a_failure()
    {
        var talk = new Talk();

        var instead = talk.Conversation.Confirm(new RemovalPreview([], [], []), false, false);

        await Assert.That(instead).IsEqualTo(ExitCode.Success);
        await Assert.That(talk.Prompt.Written).IsEquivalentTo(["nothing to remove"]);
    }

    [Test]
    public async Task Yes_sends_without_showing_or_asking()
    {
        var talk = new Talk();

        var instead = talk.Conversation.Confirm(
            OneClean,
            alreadyConfirmed: true,
            inputRedirected: false
        );

        await Assert.That(instead).IsNull();
        await Assert.That(talk.Shown).IsEmpty();
        await Assert.That(talk.Prompt.Written).IsEmpty();
    }

    /// <summary>A pipe cannot answer, and defaulting to yes for one takes assets off disk.</summary>
    [Test]
    public async Task Without_a_terminal_it_shows_the_list_and_stops_asking_for_yes()
    {
        var talk = new Talk();

        var instead = talk.Conversation.Confirm(
            OneClean,
            alreadyConfirmed: false,
            inputRedirected: true
        );

        await Assert.That(instead).IsEqualTo(ExitCode.UserError);
        await Assert.That(talk.Shown).IsEquivalentTo(OneClean.Lines);
        await Assert
            .That(talk.Errors.ToString().TrimEnd())
            .IsEqualTo(
                "sv: 1 node(s) above would be removed. "
                    + "Re-run with --yes to confirm; there is no terminal here to ask in."
            );
        await Assert.That(talk.Prompt.Written).IsEmpty();
    }

    [Test]
    public async Task At_a_terminal_yes_sends()
    {
        var talk = new Talk("y");

        var instead = talk.Conversation.Confirm(
            OneClean,
            alreadyConfirmed: false,
            inputRedirected: false
        );

        await Assert.That(instead).IsNull();
        await Assert.That(talk.Shown).IsEquivalentTo(OneClean.Lines);
        await Assert.That(talk.Prompt.Written).IsEquivalentTo(["remove 1 node(s)? [y/N] "]);
    }

    [Test]
    [Arguments("n")]
    [Arguments("")]
    [Arguments(null)]
    public async Task At_a_terminal_anything_but_yes_removes_nothing(string? answer)
    {
        var talk = new Talk(answer);

        var instead = talk.Conversation.Confirm(
            OneClean,
            alreadyConfirmed: false,
            inputRedirected: false
        );

        await Assert.That(instead).IsEqualTo(ExitCode.Success);
        await Assert
            .That(talk.Prompt.Written)
            .IsEquivalentTo(["remove 1 node(s)? [y/N] ", "nothing removed"]);
    }

    private static RemovalPreview Preview(params WorkingCopyEntry[] entries) =>
        RemovalPreview.Of(
            new StatusResponse(
                new WorkingCopyInfo(Root, "https://svn.example/repo", "uuid-1", 31),
                entries,
                ServedFromWarmIndex: true,
                ServerElapsedMilliseconds: 0,
                UnfinishedOperations: 0,
                UnrecordedMoves: []
            ),
            [Path.Combine(Root, entries[0].RelPath)],
            StringComparison.Ordinal
        );

    private static WorkingCopyEntry Entry(string relPath) =>
        new(
            relPath,
            NodeKind.File,
            NodeStatus.Unmodified,
            PropertyStatus.Unmodified,
            Revision: 1,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );

    private sealed class Talk
    {
        public Talk(params string?[] answers)
        {
            Prompt = new FakePrompt(answers);
            Conversation = new RemovalConversation(Prompt, Errors, Shown.AddRange);
        }

        public FakePrompt Prompt { get; }

        public StringWriter Errors { get; } = new();

        public List<string> Shown { get; } = [];

        public RemovalConversation Conversation { get; }
    }
}
