using Subverted.Frontend.Diff;
using TUnit.Assertions.Enums;

namespace Subverted.Frontend.Tests.Diff;

/// <summary>
/// Asserts a whole hunk: its header's four numbers and every line, in order. <see cref="Hunk"/>
/// holds a list, so record equality alone would compare references.
/// </summary>
internal static class ExpectedHunk
{
    public static async Task Matches(
        Hunk actual,
        (int OldStart, int OldCount, int NewStart, int NewCount) header,
        params DiffLine[] lines
    )
    {
        await Assert
            .That((actual.OldStart, actual.OldCount, actual.NewStart, actual.NewCount))
            .IsEqualTo(header);
        await Assert.That(actual.Lines).IsEquivalentTo(lines, CollectionOrdering.Matching);
    }

    /// <summary>A <see cref="TextChange"/> holding exactly one hunk, which must match.</summary>
    public static async Task IsOnlyHunkOf(
        FileContentChange? content,
        (int OldStart, int OldCount, int NewStart, int NewCount) header,
        params DiffLine[] lines
    )
    {
        var text = await Assert.That(content).IsTypeOf<TextChange>();
        await Assert.That(text!.Hunks).Count().IsEqualTo(1);
        await Matches(text.Hunks[0], header, lines);
    }
}
