using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Subverted.App.Presentation;
using Subverted.Frontend.Diff;

namespace Subverted.App.Views;

/// <summary>
/// A diff as a virtualised list of lines with old and new number gutters. Lines are selected like
/// list items, and the platform's copy gesture puts the selected lines' text on the clipboard.
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

    public DiffLinesView()
    {
        InitializeComponent();
    }

    public DiffDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
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
