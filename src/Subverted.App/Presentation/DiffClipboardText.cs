namespace Subverted.App.Presentation;

/// <summary>What copying a selection of diff rows puts on the clipboard.</summary>
public static class DiffClipboardText
{
    /// <returns>
    /// The selected lines' own text without their signs, in the order a unified diff lists them,
    /// whichever layout they were selected in: a side-by-side run's old lines come before its new
    /// ones. Headers and cards are skipped; with no line selected, an empty string.
    /// </returns>
    public static string Of(IReadOnlyList<DiffRow> rows, IEnumerable<int> selectedIndexes)
    {
        var copied = new List<string>();
        var newSideOfRun = new List<string>();
        foreach (var row in selectedIndexes.Order().Select(index => rows[index]))
        {
            if (row is DiffSplitRow { IsContext: false } change)
            {
                AddText(copied, change.Old?.Text);
                AddText(newSideOfRun, change.New?.Text);
                continue;
            }

            copied.AddRange(newSideOfRun);
            newSideOfRun.Clear();
            AddText(copied, TextOf(row));
        }

        copied.AddRange(newSideOfRun);
        return string.Join(Environment.NewLine, copied);
    }

    private static string? TextOf(DiffRow row) =>
        row switch
        {
            DiffTextRow text => text.Line.Text,
            DiffSplitRow context => context.Old!.Text,
            _ => null,
        };

    private static void AddText(List<string> texts, string? text)
    {
        if (text is not null)
        {
            texts.Add(text);
        }
    }
}
