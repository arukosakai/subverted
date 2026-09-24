using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class TickedPathsTests
{
    [Test]
    public async Task Nothing_is_ticked_to_begin_with()
    {
        var ticks = new TickedPaths();

        await Assert.That(ticks.IsTicked("a.png")).IsFalse();
        await Assert.That(ticks.Paths).IsEmpty();
    }

    [Test]
    public async Task Toggling_ticks_a_path_and_says_so()
    {
        var ticks = new TickedPaths();

        var ticked = ticks.Toggle("a.png");

        await Assert.That(ticked).IsTrue();
        await Assert.That(ticks.IsTicked("a.png")).IsTrue();
        await Assert.That(ticks.IsTicked("b.png")).IsFalse();
    }

    [Test]
    public async Task Toggling_again_unticks_it_and_says_so()
    {
        var ticks = new TickedPaths();
        ticks.Toggle("a.png");

        var ticked = ticks.Toggle("a.png");

        await Assert.That(ticked).IsFalse();
        await Assert.That(ticks.IsTicked("a.png")).IsFalse();
    }

    /// <summary>Paths are SVN's, and SVN is case-sensitive even where the disk is not.</summary>
    [Test]
    public async Task Paths_differing_only_in_case_are_different_ticks()
    {
        var ticks = new TickedPaths();
        ticks.Toggle("Hero.png");

        await Assert.That(ticks.IsTicked("hero.png")).IsFalse();
    }

    [Test]
    public async Task A_path_appearing_for_the_first_time_takes_its_default()
    {
        var ticks = new TickedPaths();

        ticks.Follow([Row(Entry("edit.png")), Row(Entry("new.log", NodeStatus.Unversioned))]);

        await Assert.That(ticks.Paths).IsEquivalentTo(new[] { "edit.png" });
    }

    /// <summary>The once-a-second resync must not tick back what the person unticked.</summary>
    [Test]
    public async Task An_untick_survives_every_resync_that_still_lists_the_path()
    {
        var ticks = new TickedPaths();
        ticks.Follow([Row(Entry("edit.png"))]);
        ticks.Toggle("edit.png");

        ticks.Follow([Row(Entry("edit.png"))]);
        ticks.Follow([Row(Entry("edit.png", NodeStatus.Added))]);

        await Assert.That(ticks.Paths).IsEmpty();
    }

    [Test]
    public async Task A_tick_on_something_unticked_by_default_survives_the_resync()
    {
        var ticks = new TickedPaths();
        ticks.Follow([Row(Entry("new.png", NodeStatus.Unversioned))]);
        ticks.Toggle("new.png");

        ticks.Follow([Row(Entry("new.png", NodeStatus.Unversioned))]);

        await Assert.That(ticks.Paths).IsEquivalentTo(new[] { "new.png" });
    }

    [Test]
    public async Task A_path_still_listed_keeps_its_tick_and_one_gone_loses_it()
    {
        var ticks = new TickedPaths();
        ticks.Follow([Row(Entry("kept.png")), Row(Entry("gone.png"))]);

        ticks.Follow([Row(Entry("kept.png"))]);

        await Assert.That(ticks.Paths).IsEquivalentTo(new[] { "kept.png" });
    }

    /// <summary>Committed from elsewhere, then edited again: a new change, with its default again.</summary>
    [Test]
    public async Task A_path_that_leaves_and_comes_back_takes_its_default_again()
    {
        var ticks = new TickedPaths();
        ticks.Follow([Row(Entry("edit.png"))]);
        ticks.Toggle("edit.png");
        ticks.Follow([]);

        ticks.Follow([Row(Entry("edit.png"))]);

        await Assert.That(ticks.Paths).IsEquivalentTo(new[] { "edit.png" });
    }

    /// <summary>The unversioned half of a rename can be listed a scan before the missing half.</summary>
    [Test]
    public async Task A_path_that_becomes_a_rename_row_takes_the_rename_s_default()
    {
        var ticks = new TickedPaths();
        ticks.Follow([Row(Entry("new.png", NodeStatus.Unversioned))]);

        ticks.Follow([ChangeRow.Rename(Entry("new.png", NodeStatus.Unversioned), "old.png")]);

        await Assert.That(ticks.Paths).IsEquivalentTo(new[] { "new.png" });
    }

    [Test]
    public async Task A_rename_row_already_seen_keeps_its_untick()
    {
        var rename = ChangeRow.Rename(Entry("new.png", NodeStatus.Unversioned), "old.png");
        var ticks = new TickedPaths();
        ticks.Follow([rename]);
        ticks.Toggle("new.png");

        ticks.Follow([rename]);

        await Assert.That(ticks.Paths).IsEmpty();
    }

    /// <summary>A pair that stops pairing is two unrelated changes, and `?` stays as it was left.</summary>
    [Test]
    public async Task A_rename_row_that_splits_back_is_not_a_new_path()
    {
        var ticks = new TickedPaths();
        ticks.Follow([ChangeRow.Rename(Entry("new.png", NodeStatus.Unversioned), "old.png")]);
        ticks.Toggle("new.png");

        ticks.Follow([Row(Entry("new.png", NodeStatus.Unversioned))]);

        await Assert.That(ticks.Paths).IsEmpty();
    }

    /// <summary>Resolving a conflict is saying the file is ready, so it comes back ticked (operator's call).</summary>
    [Test]
    public async Task A_conflict_that_is_resolved_is_ticked_for_commit()
    {
        var ticks = new TickedPaths();
        ticks.Follow([Row(Conflicted("hero.png"))]);
        var whileConflicted = ticks.IsTicked("hero.png");

        ticks.Follow([Row(Entry("hero.png"))]);

        await Assert.That(whileConflicted).IsFalse();
        await Assert.That(ticks.Paths).IsEquivalentTo(new[] { "hero.png" });
    }

    /// <summary>The resolve overrides an untick made before the conflict, since it is a newer decision.</summary>
    [Test]
    public async Task A_resolve_ticks_a_path_even_if_it_was_unticked_before_the_conflict()
    {
        var ticks = new TickedPaths();
        ticks.Follow([Row(Entry("hero.png"))]);
        ticks.Toggle("hero.png");
        ticks.Follow([Row(Conflicted("hero.png"))]);

        ticks.Follow([Row(Entry("hero.png"))]);

        await Assert.That(ticks.Paths).IsEquivalentTo(new[] { "hero.png" });
    }

    /// <summary>Only leaving the conflict counts: a clean edit resynced as itself keeps its untick.</summary>
    [Test]
    public async Task A_conflict_still_conflicted_is_not_ticked_by_the_resync()
    {
        var ticks = new TickedPaths();
        ticks.Follow([Row(Conflicted("hero.png"))]);

        ticks.Follow([Row(Conflicted("hero.png"))]);

        await Assert.That(ticks.Paths).IsEmpty();
    }

    /// <summary>A resolve takes the path's default, not a blanket tick: an unversioned result stays unticked.</summary>
    [Test]
    public async Task A_resolve_that_leaves_something_unticked_by_default_does_not_tick_it()
    {
        var ticks = new TickedPaths();
        ticks.Follow([Row(Conflicted("hero.png"))]);

        ticks.Follow([Row(Entry("hero.png", NodeStatus.Unversioned))]);

        await Assert.That(ticks.Paths).IsEmpty();
    }

    [Test]
    public async Task Unticking_after_a_commit_clears_only_what_was_sent_and_it_stays_clear()
    {
        var ticks = new TickedPaths();
        ticks.Follow([Row(Entry("sent.png")), Row(Entry("kept.png"))]);

        ticks.Untick(["sent.png"]);
        ticks.Follow([Row(Entry("sent.png")), Row(Entry("kept.png"))]);

        await Assert.That(ticks.Paths).IsEquivalentTo(new[] { "kept.png" });
    }

    private static ChangeRow Row(WorkingCopyEntry entry) => ChangeRow.From(entry);

    private static WorkingCopyEntry Conflicted(string relPath) =>
        Entry(relPath, NodeStatus.Conflicted, isConflicted: true);
}
