using Subverted.Core;

namespace Subverted.Cli.Tests;

/// <summary>
/// The conversation itself, driven without a terminal. What it owns is the handling of an answer
/// that is not one — which is exactly the case nobody tests by hand, because typing a typo into a
/// prompt is not what people do when they are checking whether something works.
/// </summary>
public sealed class PickConversationTests
{
    private const StringComparison Sensitive = StringComparison.Ordinal;

    [Test]
    public async Task Every_node_is_asked_about_and_the_answers_decide_what_is_picked()
    {
        var picker = Picker(Modified("src/a.txt"), Modified("src/b.txt"));
        var prompt = new FakePrompt("y", "n");

        PickConversation.Walk(picker, StatusPalette.Plain, prompt, 2);

        await Assert
            .That(picker.Picked.Select(entry => entry.RelPath))
            .IsEquivalentTo(["src/a.txt"]);
        await Assert.That(prompt.Written).Contains("M       src/a.txt  send? [y,n,a,d,q,?] ");
        await Assert.That(prompt.Written).Contains("M       src/b.txt  send? [y,n,a,d,q,?] ");
    }

    [Test]
    public async Task The_opening_line_is_written_once_before_the_first_question()
    {
        var picker = Picker(Modified("src/a.txt"));

        var prompt = new FakePrompt("y");
        PickConversation.Walk(picker, StatusPalette.Plain, prompt, 1);

        await Assert.That(prompt.Written[0]).IsEqualTo(PickReport.Opening(1));
        await Assert.That(prompt.Written.Count(PickReport.Opening(1).Equals)).IsEqualTo(1);
    }

    /// <summary>
    /// A typo must not decide a node. It says what the letters mean and asks the same one again —
    /// moving on either way would commit or drop a file on the strength of a slip.
    /// </summary>
    [Test]
    public async Task An_answer_it_cannot_read_explains_the_letters_and_asks_the_same_node_again()
    {
        var picker = Picker(Modified("src/a.txt"));
        var prompt = new FakePrompt("yep", "y");

        PickConversation.Walk(picker, StatusPalette.Plain, prompt, 1);

        await Assert
            .That(picker.Picked.Select(entry => entry.RelPath))
            .IsEquivalentTo(["src/a.txt"]);
        await Assert
            .That(prompt.Written.Count("M       src/a.txt  send? [y,n,a,d,q,?] ".Equals))
            .IsEqualTo(2);
        await Assert.That(prompt.Written).Contains(PickReport.Explanation[0]);
    }

    [Test]
    public async Task Asking_what_the_letters_mean_does_the_same_without_being_a_mistake()
    {
        var picker = Picker(Modified("src/a.txt"));
        var prompt = new FakePrompt("?", "n");

        PickConversation.Walk(picker, StatusPalette.Plain, prompt, 1);

        await Assert.That(picker.Picked).IsEmpty();
        await Assert.That(picker.Abandoned).IsFalse();
        await Assert.That(prompt.Written).Contains(PickReport.Explanation[0]);
    }

    /// <summary>
    /// End of input ends the walk. Treating it as unreadable would re-ask a question with nobody
    /// left to answer it, which is a loop that never returns.
    /// </summary>
    [Test]
    public async Task End_of_input_ends_the_walk_rather_than_asking_forever()
    {
        var picker = Picker(Modified("src/a.txt"), Modified("src/b.txt"));
        var prompt = new FakePrompt([null]);

        PickConversation.Walk(picker, StatusPalette.Plain, prompt, 2);

        await Assert.That(picker.Abandoned).IsTrue();
        await Assert.That(picker.Picked).IsEmpty();
    }

    [Test]
    public async Task Quitting_stops_the_questions_at_once()
    {
        var picker = Picker(Modified("src/a.txt"), Modified("src/b.txt"), Modified("src/c.txt"));
        var prompt = new FakePrompt("y", "q");

        PickConversation.Walk(picker, StatusPalette.Plain, prompt, 3);

        await Assert.That(picker.Abandoned).IsTrue();
        await Assert.That(picker.Picked).IsEmpty();
    }

    [Test]
    public async Task Nothing_to_ask_about_writes_the_opening_line_and_asks_nothing()
    {
        var picker = new ChangePicker([], Sensitive);
        var prompt = new FakePrompt();

        PickConversation.Walk(picker, StatusPalette.Plain, prompt, 0);

        await Assert.That(prompt.Written).IsEquivalentTo([PickReport.Opening(0)]);
    }

    private static ChangePicker Picker(params WorkingCopyEntry[] candidates) =>
        new(candidates, Sensitive);

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
}
