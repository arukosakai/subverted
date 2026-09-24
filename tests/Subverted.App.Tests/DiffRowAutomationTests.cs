using Subverted.App.Presentation;
using Subverted.Frontend.Diff;
using static Subverted.App.Tests.Diffs;

namespace Subverted.App.Tests;

/// <summary>What a screen reader hears for each kind of diff row: its content, never its record.</summary>
public sealed class DiffRowAutomationTests
{
    [Test]
    public async Task An_added_line_says_its_side_its_new_number_and_its_text()
    {
        var row = new DiffTextRow(Added(4, "public int Health = 120;"));

        await Assert.That(row.AutomationName).IsEqualTo("Added line 4: public int Health = 120;");
    }

    [Test]
    public async Task A_removed_line_says_its_side_its_old_number_and_its_text()
    {
        var row = new DiffTextRow(Removed(3, "speed"));

        await Assert.That(row.AutomationName).IsEqualTo("Removed line 3: speed");
    }

    /// <summary>A context line moved by an edit above it is heard where it now is.</summary>
    [Test]
    public async Task A_context_line_says_its_number_in_the_working_copy()
    {
        var row = new DiffTextRow(Context(5, 6, "}"));

        await Assert.That(row.AutomationName).IsEqualTo("Line 6: }");
    }

    [Test]
    public async Task A_side_by_side_context_row_is_said_once()
    {
        var row = new DiffSplitRow(Context(1, 1, "{"), Context(1, 1, "{"));

        await Assert.That(row.AutomationName).IsEqualTo("Line 1: {");
    }

    [Test]
    public async Task A_side_by_side_change_says_the_old_side_then_the_new()
    {
        var row = new DiffSplitRow(Removed(3, "Velocity"), Added(3, "Speed"));

        await Assert
            .That(row.AutomationName)
            .IsEqualTo("Removed line 3: Velocity; Added line 3: Speed");
    }

    [Test]
    public async Task A_side_by_side_row_padded_on_one_side_says_only_the_other()
    {
        await Assert
            .That(new DiffSplitRow(null, Added(9, "ninth")).AutomationName)
            .IsEqualTo("Added line 9: ninth");
        await Assert
            .That(new DiffSplitRow(Removed(9, "ninth"), null).AutomationName)
            .IsEqualTo("Removed line 9: ninth");
    }

    [Test]
    public async Task The_other_rows_say_what_they_show()
    {
        await Assert
            .That(new DiffHunkRow("@@ -1,6 +1,6 @@").AutomationName)
            .IsEqualTo("@@ -1,6 +1,6 @@");
        await Assert
            .That(new DiffFileHeaderRow("src/Player.cs").AutomationName)
            .IsEqualTo("src/Player.cs");
        await Assert.That(new DiffFileHeaderRow("").AutomationName).IsEqualTo("This folder");
        await Assert
            .That(new DiffNoLinesRow("empty.txt").AutomationName)
            .IsEqualTo("No lines to show.");
        await Assert
            .That(new DiffPropertySectionRow("a.txt").AutomationName)
            .IsEqualTo("Property changes");
        await Assert
            .That(new DiffPropertyRow("svn:eol-style", PropertyChangeKind.Added).AutomationName)
            .IsEqualTo("svn:eol-style, Added");
        await Assert
            .That(new DiffBinaryRow("art/hero.png", "image/png", IsOnlyFile: true).AutomationName)
            .IsEqualTo("Binary file, image/png");
    }
}
