using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using Subverted.App.ViewModels;

namespace Subverted.App.Views;

/// <summary>The output log in the bottom strip. A new line scrolls into view, as a terminal does.</summary>
public sealed partial class OutputLogView : UserControl
{
    private OutputLogViewModel? _shown;

    public OutputLogView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow(DataContext as OutputLogViewModel);
    }

    private void Follow(OutputLogViewModel? log)
    {
        if (_shown is not null)
        {
            _shown.Lines.CollectionChanged -= ScrollToNewest;
        }

        _shown = log;
        if (log is not null)
        {
            log.Lines.CollectionChanged += ScrollToNewest;
        }
    }

    private void ScrollToNewest(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
}
