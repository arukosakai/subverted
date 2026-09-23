using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class CommitButtonTextTests
{
    [Test]
    [Arguments(-1, "Commit")]
    [Arguments(0, "Commit")]
    [Arguments(1, "Commit 1 file")]
    [Arguments(2, "Commit 2 files")]
    [Arguments(1200, "Commit 1,200 files")]
    public async Task The_button_says_how_many_rows_it_sends(int rows, string text)
    {
        await Assert.That(CommitButtonText.For(rows)).IsEqualTo(text);
    }
}
