using System.Globalization;
using Avalonia.Data.Converters;
using Subverted.App.ViewModels;

namespace Subverted.App.Views;

/// <summary>Shows a part of the diff pane only in the state it belongs to.</summary>
public sealed class DiffPaneStateIs(DiffPaneState state) : IValueConverter
{
    public static readonly DiffPaneStateIs NothingSelected = new(DiffPaneState.NothingSelected);

    public static readonly DiffPaneStateIs Loading = new(DiffPaneState.Loading);

    public static readonly DiffPaneStateIs Ready = new(DiffPaneState.Ready);

    public static readonly DiffPaneStateIs NothingToShow = new(DiffPaneState.NothingToShow);

    public static readonly DiffPaneStateIs Unreachable = new(DiffPaneState.Unreachable);

    public static readonly DiffPaneStateIs Failed = new(DiffPaneState.Failed);

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is DiffPaneState shown && shown == state;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException("Visibility does not convert back to a state.");
}
