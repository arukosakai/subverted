using System.Globalization;
using Avalonia.Data.Converters;
using Subverted.App.Presentation;

namespace Subverted.App.Views;

/// <summary>Whether the diff is laid out as <paramref name="layout"/>: its style class and its toggle's state.</summary>
public sealed class LayoutIs(DiffLayout layout) : IValueConverter
{
    public static readonly LayoutIs Split = new(DiffLayout.Split);

    public static readonly LayoutIs Unified = new(DiffLayout.Unified);

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => ReferenceEquals(value, layout);

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("A style class does not convert back to a layout.");
}
