using Avalonia.Controls;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;

namespace Subverted.App.Views;

/// <summary>
/// The History view. Its one piece of code reports the revision list's scroll position; whether
/// that is near enough the end to read more is <see cref="ScrollPosition"/>'s decision.
/// </summary>
public sealed partial class HistoryView : UserControl
{
    /// <summary>About eight rows before the end, so the next page lands before anyone reaches it.</summary>
    public const double LoadAheadPixels = 320;

    public HistoryView()
    {
        InitializeComponent();
        Revisions.AddHandler(ScrollViewer.ScrollChangedEvent, OnRevisionsScrolled);
    }

    /// <remarks>Only a <see cref="ScrollViewer"/> raises this event, so its source is always one.</remarks>
    private void OnRevisionsScrolled(object? sender, ScrollChangedEventArgs e)
    {
        var scroller = (ScrollViewer)e.Source!;
        if (
            DataContext is HistoryViewModel history
            && ScrollPosition.IsNearEnd(
                scroller.Offset.Y,
                scroller.Viewport.Height,
                scroller.Extent.Height,
                LoadAheadPixels
            )
        )
        {
            history.LoadMoreCommand.Execute(null);
        }
    }
}
