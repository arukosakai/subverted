using Subverted.Frontend.Diff;

namespace Subverted.App.Presentation;

/// <summary>
/// How a diff's lines become rows: <see cref="Split"/> puts old beside new, <see cref="Unified"/>
/// lists both in one column as <c>svn diff</c> prints them. Everything else is laid out alike.
/// </summary>
public sealed class DiffLayout
{
    public static readonly DiffLayout Split = new(SplitLines.Of);

    public static readonly DiffLayout Unified = new(UnifiedLines.Of);

    private readonly Func<IReadOnlyList<DiffLine>, IEnumerable<DiffRow>> _rowsOfLines;

    private DiffLayout(Func<IReadOnlyList<DiffLine>, IEnumerable<DiffRow>> rowsOfLines)
    {
        _rowsOfLines = rowsOfLines;
    }

    /// <param name="subject">
    /// The path the pane names, spelled as the diff spells its own section: a file's diff covers
    /// only it, a directory's names each file inside.
    /// </param>
    public IReadOnlyList<DiffRow> RowsOf(DiffDocument document, string subject) =>
        DiffRows.Of(document, subject, _rowsOfLines);
}
