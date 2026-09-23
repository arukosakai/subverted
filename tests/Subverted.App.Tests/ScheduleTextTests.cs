using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

public sealed class ScheduleTextTests
{
    [Test]
    public async Task Nothing_marked_has_no_text()
    {
        await Assert.That(ScheduleText.Of(new SelectionSchedule([], [], []))).IsNull();
    }

    [Test]
    [Arguments(1, 0, 0, "1 rename recorded")]
    [Arguments(2, 0, 0, "2 renames recorded")]
    [Arguments(0, 3, 0, "3 added")]
    [Arguments(0, 0, 1, "1 deleted")]
    [Arguments(1, 2, 3, "1 rename recorded, 2 added, 3 deleted")]
    public async Task Each_kind_of_mark_is_counted_in_the_order_they_run(
        int moved,
        int added,
        int deleted,
        string text
    )
    {
        var schedule = new SelectionSchedule(
            [.. Enumerable.Range(0, added).Select(index => $"a{index}")],
            [.. Enumerable.Range(0, deleted).Select(index => $"d{index}")],
            [
                .. Enumerable
                    .Range(0, moved)
                    .Select(index => new RecordedMove($"f{index}", $"t{index}")),
            ]
        );

        await Assert.That(ScheduleText.Of(schedule)).IsEqualTo(text);
    }
}
