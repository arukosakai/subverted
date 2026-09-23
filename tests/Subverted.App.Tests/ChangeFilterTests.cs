using Subverted.App.Presentation;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class ChangeFilterTests
{
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Blank_text_keeps_every_row(string text)
    {
        await Assert.That(ChangeFilter.Keeps(Row("art/hero.png"), text)).IsTrue();
    }

    [Test]
    [Arguments("hero", true)]
    [Arguments("art/", true)]
    [Arguments("HERO.PNG", true)]
    [Arguments("  hero  ", true)]
    [Arguments("villain", false)]
    [Arguments("art\\hero", false)]
    public async Task A_row_is_kept_when_its_path_contains_the_text_in_any_case(
        string text,
        bool kept
    )
    {
        await Assert.That(ChangeFilter.Keeps(Row("art/hero.png"), text)).IsEqualTo(kept);
    }

    [Test]
    public async Task Applying_keeps_the_matching_rows_in_the_order_they_came()
    {
        var kept = ChangeFilter.Apply([Row("src/z.cs"), Row("art/a.png"), Row("src/b.cs")], "src");

        await Assert
            .That(string.Join(",", kept.Select(row => row.RelPath)))
            .IsEqualTo("src/z.cs,src/b.cs");
    }

    private static ChangeRow Row(string relPath) => ChangeRow.From(Entry(relPath));
}
