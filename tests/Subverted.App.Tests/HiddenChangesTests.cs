using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class HiddenChangesTests
{
    [Test]
    [Arguments(0, 0)]
    [Arguments(5, 5)]
    public async Task Nothing_hidden_says_nothing_at_all(int listed, int shown)
    {
        await Assert.That(HiddenChanges.Text(listed, shown)).IsNull();
    }

    [Test]
    [Arguments(5, 4, "1 change hidden")]
    [Arguments(5, 3, "2 changes hidden")]
    [Arguments(2166, 0, "2,166 changes hidden")]
    public async Task The_line_counts_what_the_filter_keeps_out_of_sight(
        int listed,
        int shown,
        string expected
    )
    {
        await Assert.That(HiddenChanges.Text(listed, shown)).IsEqualTo(expected);
    }
}
