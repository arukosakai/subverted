using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Subverted.App.ViewModels;

namespace Subverted.App.Views;

/// <summary>
/// The commit review, as its own window. Everything it decides is in
/// <see cref="CommitReviewViewModel"/>; this closes when that asks, and passes Space to the ticks.
/// </summary>
public sealed partial class CommitReviewWindow : Window
{
    public CommitReviewWindow()
    {
        InitializeComponent();
    }

    public CommitReviewWindow(CommitReviewViewModel review)
        : this()
    {
        DataContext = review;
        review.CloseRequested += Close;
        Closed += (_, _) => review.CloseRequested -= Close;
        Opened += (_, _) => MessageBox.Focus();

        // Tunnelling, as in the table, so Space reaches the tick before the ListBox selects with it.
        List.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (
            DataContext is not CommitReviewViewModel review
            || e.Key != Key.Space
            || e.KeyModifiers != KeyModifiers.None
        )
        {
            return;
        }

        e.Handled = true;
        if (review.ToggleTickCommand.CanExecute(review.SelectedChange))
        {
            review.ToggleTickCommand.Execute(review.SelectedChange);
        }
    }
}
