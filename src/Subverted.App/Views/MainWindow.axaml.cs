using Avalonia.Controls;
using Subverted.App.ViewModels;

namespace Subverted.App.Views;

/// <summary>
/// The window, and the one place that turns window events into view-model calls. Everything it
/// decides is in <see cref="MainWindowViewModel"/>; this only reports what the platform said.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.StartAsync(CancellationToken.None);
        Activated += async (_, _) => await viewModel.ActivatedAsync(CancellationToken.None);
        Deactivated += async (_, _) => await viewModel.DeactivatedAsync();
        Closed += async (_, _) => await viewModel.DisposeAsync();
    }
}
