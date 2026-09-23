using System.Globalization;
using Avalonia.Data.Converters;
using Subverted.App.Presentation;

namespace Subverted.App.Views;

/// <summary>A byte count as a person reads it; <c>null</c> for a file that is not on disk.</summary>
public sealed class FileSizeText : IValueConverter
{
    public static readonly FileSizeText Human = new();

    private FileSizeText() { }

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is long bytes ? FileSize.Format(bytes, culture) : null;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("A size's text does not convert back to a size.");
}
