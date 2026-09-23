using CommunityToolkit.Mvvm.ComponentModel;
using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>
/// One line of the directory pane as the control holds it: the same instance while its folder
/// holds changes, so a count moving on the resync does not take the pane's selection with it.
/// </summary>
public sealed partial class FolderEntry(FolderLine content)
    : ObservableObject,
        IListSlot<FolderLine>
{
    [ObservableProperty]
    public partial FolderLine Content { get; set; } = content;
}
