using System.Globalization;
using Avalonia.Data.Converters;
using Subverted.App.ViewModels;

namespace Subverted.App.Views;

/// <summary>Shows a part of the History list only in the state it belongs to.</summary>
public sealed class HistoryStateIs(HistoryState state) : IValueConverter
{
    public static readonly HistoryStateIs Loading = new(HistoryState.Loading);

    public static readonly HistoryStateIs Ready = new(HistoryState.Ready);

    public static readonly HistoryStateIs Unreachable = new(HistoryState.Unreachable);

    public static readonly HistoryStateIs Failed = new(HistoryState.Failed);

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is HistoryState shown && shown == state;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("Visibility does not convert back to a state.");
}
