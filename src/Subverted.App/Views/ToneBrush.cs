using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Subverted.App.Presentation;

namespace Subverted.App.Views;

/// <summary>
/// A tone's brush from the theme, as <c>Tone.Modified</c> or its tinted fill <c>Tone.Modified.Soft</c>.
/// Resolved against the running theme variant, so light and dark each get their own.
/// </summary>
public sealed class ToneBrush(string suffix) : IValueConverter
{
    public static readonly ToneBrush Solid = new(string.Empty);

    public static readonly ToneBrush Soft = new(".Soft");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ChangeTone tone)
        {
            return Brushes.Transparent;
        }

        // A converter only ever runs inside a running application.
        var application = Application.Current!;
        return application.TryGetResource(
            $"Tone.{tone}{suffix}",
            application.ActualThemeVariant,
            out var brush
        )
            ? brush
            : Brushes.Transparent;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("A tone's brush does not convert back to a tone.");
}
