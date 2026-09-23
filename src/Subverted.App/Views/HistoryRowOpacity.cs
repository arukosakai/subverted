using System.Globalization;
using Avalonia.Data.Converters;

namespace Subverted.App.Views;

/// <summary>Draws a revision this working copy has not been updated to more faintly than the rest.</summary>
public sealed class HistoryRowOpacity : IValueConverter
{
    public const double NotInCopy = 0.55;

    public static readonly HistoryRowOpacity ForNotInCopy = new();

    private HistoryRowOpacity() { }

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is true ? NotInCopy : 1.0;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("Opacity does not convert back to presence.");
}
