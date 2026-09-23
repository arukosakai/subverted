using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class ScrollPositionTests
{
    [Test]
    public async Task Scrolled_to_exactly_the_lead_before_the_end_asks_for_more()
    {
        await Assert.That(ScrollPosition.IsNearEnd(680, 300, 1300, 320)).IsTrue();
    }

    [Test]
    public async Task One_pixel_short_of_the_lead_does_not_ask_yet()
    {
        await Assert.That(ScrollPosition.IsNearEnd(679, 300, 1300, 320)).IsFalse();
    }

    /// <summary>A page that does not fill the list has nothing to scroll, so it asks straight away.</summary>
    [Test]
    public async Task A_list_shorter_than_its_viewport_is_already_near_its_end()
    {
        await Assert.That(ScrollPosition.IsNearEnd(0, 600, 400, 320)).IsTrue();
    }
}
