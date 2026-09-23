using Avalonia.Data.Converters;

namespace Subverted.App.Views;

/// <summary>How far a line of the tree is pushed in: one step per folder above it.</summary>
public static class TreeIndent
{
    public const double Step = 18;

    public static readonly IValueConverter Width = new FuncValueConverter<int, double>(depth =>
        depth * Step
    );
}
