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
    [NotifyPropertyChangedFor(
        nameof(Key),
        nameof(Row),
        nameof(CanTick),
        nameof(IsFolder),
        nameof(IsTickable),
        nameof(IsHeldByConflict),
        nameof(TickMark)
    )]
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

    /// <summary>
    /// A conflict, which SVN will not commit: its tick is kept for after the resolve, but until
    /// then it shows unticked and cannot be changed.
    /// </summary>
    public bool IsHeldByConflict => Content.Row?.Entry.IsConflicted == true;

    /// <summary>What the tick box shows: the tick, or neither ticked nor unticked when not a choice.</summary>
    public bool? TickMark =>
        IsDecidedByFolder ? null
        : IsHeldByConflict ? false
        : IsTicked;

    /// <summary>A change whose tick is the person's to set.</summary>
    public bool IsTickable => CanTick && !IsDecidedByFolder && !IsHeldByConflict;

    public string Key => Content.Key;

    /// <summary>The change on this line; <c>null</c> for a folder that only holds others.</summary>
    public ChangeRow? Row => Content.Row;

    /// <summary>
    /// A folder that only holds others is not a change, and neither is an unmodified file listed
    /// among them: neither has anything to tick.
    /// </summary>
    public bool CanTick => Content.Row is { IsUnmodified: false };

    /// <summary>A line for a folder that only holds others, drawn with a folder where a tick would be.</summary>
    public bool IsFolder => Content.Row is null;
}
