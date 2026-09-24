using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Subverted.App.Presentation;
using Subverted.Frontend.Diff;

namespace Subverted.App.Views;

/// <summary>
/// A diff as a virtualised list of lines with old and new number gutters, side by side or in one
/// column. Lines are selected like list items, and the platform's copy gesture puts the selected
/// lines' text on the clipboard.
/// </summary>
public sealed partial class DiffLinesView : UserControl
{
    public static readonly StyledProperty<DiffDocument?> DocumentProperty =
        AvaloniaProperty.Register<DiffLinesView, DiffDocument?>(nameof(Document));

    /// <summary>The size a binary card shows; it describes the document's file only when there is one.</summary>
    public static readonly StyledProperty<long?> SizeInBytesProperty = AvaloniaProperty.Register<
        DiffLinesView,
        long?
    >(nameof(SizeInBytes));

    public static readonly StyledProperty<ICommand?> OpenInAppCommandProperty =
        AvaloniaProperty.Register<DiffLinesView, ICommand?>(nameof(OpenInAppCommand));

    /// <summary>Old beside new unless someone picks the unified column.</summary>
    public static readonly StyledProperty<DiffLayout> LayoutProperty = AvaloniaProperty.Register<
        DiffLinesView,
        DiffLayout
    >(nameof(Layout), DiffLayout.Split);

    /// <summary>What the left side of the split layout shows, named over its column.</summary>
    public static readonly StyledProperty<string> OldTitleProperty = AvaloniaProperty.Register<
        DiffLinesView,
        string
    >(nameof(OldTitle), "Before");

    /// <summary>What the right side of the split layout shows, named over its column.</summary>
    public static readonly StyledProperty<string> NewTitleProperty = AvaloniaProperty.Register<
        DiffLinesView,
        string
    >(nameof(NewTitle), "After");

    public DiffLinesView()
    {
        InitializeComponent();
    }

    public DiffDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public DiffLayout Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public string OldTitle
    {
        get => GetValue(OldTitleProperty);
        set => SetValue(OldTitleProperty, value);
    }

    public string NewTitle
    {
        get => GetValue(NewTitleProperty);
        set => SetValue(NewTitleProperty, value);
    }

    public long? SizeInBytes
    {
        get => GetValue(SizeInBytesProperty);
        set => SetValue(SizeInBytesProperty, value);
    }

    public ICommand? OpenInAppCommand
    {
        get => GetValue(OpenInAppCommandProperty);
        set => SetValue(OpenInAppCommandProperty, value);
    }

    private void OnSplitClicked(object? sender, RoutedEventArgs e) => Layout = DiffLayout.Split;

    private void OnUnifiedClicked(object? sender, RoutedEventArgs e) => Layout = DiffLayout.Unified;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var topLevel = TopLevel.GetTopLevel(this);
        if (
            e.Handled
            || topLevel?.Clipboard is not { } clipboard
            || Application.Current?.PlatformSettings?.HotkeyConfiguration.Copy.Any(copy =>
                copy.Matches(e)
            )
                is not true
            || Lines.ItemsSource is not IReadOnlyList<DiffRow> rows
        )
        {
            return;
        }

        e.Handled = true;
        _ = clipboard.SetTextAsync(DiffClipboardText.Of(rows, Lines.Selection.SelectedIndexes));
    }
}
