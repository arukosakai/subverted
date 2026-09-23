using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Subverted.App.ViewModels;

namespace Subverted.App.Views;

public sealed partial class WorkingCopyView : UserControl
{
    public WorkingCopyView()
    {
        InitializeComponent();

        // Tunnelling, so Space and Enter reach the list's own commands before the ListBox or the
        // focused item treats them as selection keys. ↑/↓ are left to the ListBox.
        List.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not WorkingCopyViewModel view || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        ICommand? command = e.Key switch
        {
            Key.Space => view.ToggleTickCommand,
            Key.Enter => view.OpenCommand,
            _ => null,
        };
        if (command is null)
        {
            return;
        }

        e.Handled = true;
        if (command.CanExecute(view.SelectedEntry))
        {
            command.Execute(view.SelectedEntry);
        }
    }
}
