using Subverted.App.Presentation;

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
    public async Task A_path_still_listed_keeps_its_tick_and_one_gone_loses_it()
    {
        var ticks = new TickedPaths();
        ticks.Toggle("kept.png");
        ticks.Toggle("gone.png");

        ticks.KeepOnly(["kept.png", "never-ticked.png"]);

        await Assert.That(ticks.Paths).IsEquivalentTo(new[] { "kept.png" });
    }
}
