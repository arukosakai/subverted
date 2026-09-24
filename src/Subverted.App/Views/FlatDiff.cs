using System.Globalization;
using Avalonia.Data.Converters;
using Subverted.App.Presentation;
using Subverted.Frontend.Diff;

namespace Subverted.App.Views;

/// <summary>
/// A <see cref="DiffDocument"/>, a <see cref="DiffLayout"/> and the subject the pane names, in that
/// order, as the rows <see cref="DiffLinesView"/> lists; none unless all three are given.
/// </summary>
public sealed class FlatDiff : IMultiValueConverter
{
    public static readonly FlatDiff Rows = new();

    private FlatDiff() { }

    public object? Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) =>
        values is [DiffDocument document, DiffLayout layout, string subject]
            ? layout.RowsOf(document, subject)
            : Array.Empty<DiffRow>();
}
