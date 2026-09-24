using System.Globalization;
using Avalonia.Data.Converters;
using Subverted.App.Presentation;
using Subverted.Frontend.Diff;

namespace Subverted.App.Views;

/// <summary>
/// A <see cref="DiffDocument"/> and a <see cref="DiffLayout"/>, in that order, as the rows
/// <see cref="DiffLinesView"/> lists; none unless both are given.
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
        values is [DiffDocument document, DiffLayout layout]
            ? layout.RowsOf(document)
            : Array.Empty<DiffRow>();
}
