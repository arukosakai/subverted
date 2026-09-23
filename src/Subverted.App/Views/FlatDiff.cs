using System.Globalization;
using Avalonia.Data.Converters;
using Subverted.App.Presentation;
using Subverted.Frontend.Diff;

namespace Subverted.App.Views;

/// <summary>A <see cref="DiffDocument"/> as the rows <see cref="DiffLinesView"/> lists; none without one.</summary>
public sealed class FlatDiff : IValueConverter
{
    public static readonly FlatDiff Rows = new();

    private FlatDiff() { }

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is DiffDocument document ? DiffRows.Of(document) : Array.Empty<DiffRow>();

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("Rows do not convert back to a document.");
}
