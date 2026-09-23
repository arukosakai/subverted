using Subverted.App.Presentation;
using static Subverted.App.Tests.Diffs;

namespace Subverted.App.Tests;

public sealed class DiffClipboardTextTests
{
    private static readonly DiffRow[] Rows =
    [
        new DiffHunkRow("@@ -1,2 +1,2 @@"),
        new DiffTextRow(Context(1, 1, "first")),
        new DiffTextRow(Removed(2, "second")),
        new DiffTextRow(Added(2, "second, changed")),
        new DiffPropertySectionRow("a.txt"),
        new DiffPropertyRow("svn:eol-style", Frontend.Diff.PropertyChangeKind.Added),
        new DiffTextRow(Added(1, "native")),
    ];

    /// <summary>Selection order is click order; the clipboard gets the lines as the file has them.</summary>
    [Test]
    public async Task Selected_lines_are_copied_in_list_order_without_their_signs()
    {
        await Assert
            .That(DiffClipboardText.Of(Rows, [3, 1, 2]))
            .IsEqualTo(string.Join(Environment.NewLine, "first", "second", "second, changed"));
    }

    [Test]
    public async Task Headers_in_the_selection_are_left_out()
    {
        await Assert
            .That(DiffClipboardText.Of(Rows, [0, 1, 4, 5, 6]))
            .IsEqualTo(string.Join(Environment.NewLine, "first", "native"));
    }

    [Test]
    public async Task Nothing_selected_copies_nothing()
    {
        await Assert.That(DiffClipboardText.Of(Rows, [])).IsEqualTo(string.Empty);
    }
}
