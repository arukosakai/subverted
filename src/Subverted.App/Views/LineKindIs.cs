using System.Globalization;
using Avalonia.Data.Converters;
using Subverted.Frontend.Diff;

namespace Subverted.App.Views;

/// <summary>Puts a diff line's style class on it: added and removed lines are tinted, context is not.</summary>
public sealed class LineKindIs(DiffLineKind kind) : IValueConverter
{
    public static readonly LineKindIs Added = new(DiffLineKind.Added);

    public static readonly LineKindIs Removed = new(DiffLineKind.Removed);

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is DiffLineKind line && line == kind;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("A style class does not convert back to a line kind.");
}
