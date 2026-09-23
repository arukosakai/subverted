namespace Subverted.App.Presentation;

/// <summary>What copying a selection of diff rows puts on the clipboard.</summary>
public static class DiffClipboardText
{
    /// <returns>
    /// The selected text lines' own text in list order, without their signs, so it pastes back as
    /// the file had it. Headers and cards are skipped; with no text line selected, an empty string.
    /// </returns>
    public static string Of(IReadOnlyList<DiffRow> rows, IEnumerable<int> selectedIndexes) =>
        string.Join(
            Environment.NewLine,
            selectedIndexes
                .Order()
                .Select(index => rows[index])
                .OfType<DiffTextRow>()
                .Select(row => row.Line.Text)
        );
}
