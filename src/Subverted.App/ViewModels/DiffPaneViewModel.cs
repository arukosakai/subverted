using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subverted.App.Presentation;
using Subverted.Frontend.Diff;

namespace Subverted.App.ViewModels;

/// <summary>The right-hand pane: the selected row's local changes, fetched fresh from the daemon.</summary>
public sealed partial class DiffPaneViewModel : ObservableObject
{
    [ObservableProperty]
    public partial DiffPaneState State { get; private set; } = DiffPaneState.NothingSelected;

    /// <summary>The row whose diff is shown, which carries the badge the pane's header draws.</summary>
    [ObservableProperty]
    public partial ChangeRow? Row { get; private set; }

    [ObservableProperty]
    public partial DiffDocument? Document { get; private set; }

    /// <summary>Why the pane is not showing a diff, when it is not.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>The file's size on disk, for the binary card; <c>null</c> when it is not there.</summary>
    [ObservableProperty]
    public partial long? SizeInBytes { get; private set; }

    /// <summary>Opens the selected file in whatever the system opens it with.</summary>
    [RelayCommand]
    private void OpenInApp() { }
}
