namespace Subverted.App.Presentation;

/// <summary>
/// A place in a live list that keeps its identity while what it shows changes. The list control
/// holds on to the slot — its container, selection and keyboard focus — and only the content moves.
/// </summary>
public interface IListSlot<T>
{
    T Content { get; set; }
}
