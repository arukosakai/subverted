using Subverted.App.Presentation;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class FlatChangeListTests
{
    [Test]
    public async Task One_line_per_change_in_the_order_given()
    {
        var rows = new[] { ChangeRow.From(Entry("z.txt")), ChangeRow.From(Entry("art/a.png")) };

        var lines = FlatChangeList.Of(rows);

        await Assert.That(lines.SequenceEqual(rows.Select(ChangeListItem.Flat))).IsTrue();
    }
}
