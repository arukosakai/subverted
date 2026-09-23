using CommunityToolkit.Mvvm.ComponentModel;
using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>
/// One line of the list as the control holds it. It stays the same instance while its path is
/// shown, so a resync or a tick updates it in place instead of replacing the line under the focus.
/// </summary>
public sealed partial class ChangeListEntry(ChangeListItem content)
    : ObservableObject,
        IListSlot<ChangeListItem>
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Key), nameof(Row), nameof(CanTick), nameof(IsTickable))]
    public partial ChangeListItem Content { get; set; } = content;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TickMark))]
    public partial bool IsTicked { get; set; }

    /// <summary>
    /// A directory above this line has already settled whether it goes (D20), so its tick is not a
    /// choice and is not sent.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TickMark), nameof(IsTickable))]
    public partial bool IsDecidedByFolder { get; set; }

    /// <summary>What the tick box shows: the tick, or neither ticked nor unticked when not a choice.</summary>
    public bool? TickMark => IsDecidedByFolder ? null : IsTicked;

    /// <summary>A change whose tick is the person's to set.</summary>
    public bool IsTickable => CanTick && !IsDecidedByFolder;

    public string Key => Content.Key;

    /// <summary>The change on this line; <c>null</c> for a folder that only holds others.</summary>
    public ChangeRow? Row => Content.Row;

    /// <summary>A folder that only holds others is not a change, so it has nothing to tick.</summary>
    public bool CanTick => Content.Row is not null;
}
