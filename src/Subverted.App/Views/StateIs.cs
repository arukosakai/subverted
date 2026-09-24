using System.Globalization;
using Avalonia.Data.Converters;
using Subverted.App.ViewModels;

namespace Subverted.App.Views;

/// <summary>Shows a part of the view only in the states it belongs to.</summary>
public sealed class StateIs(params WorkingCopyState[] states) : IValueConverter
{
    public static readonly StateIs Loading = new(WorkingCopyState.Loading);

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is WorkingCopyState state && states.Contains(state);

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("Visibility does not convert back to a state.");
}
