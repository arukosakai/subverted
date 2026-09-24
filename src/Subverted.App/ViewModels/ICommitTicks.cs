namespace Subverted.App.ViewModels;

/// <summary>
/// The table's ticks as the review window reads and changes them: one set, not a copy, so a tick
/// dropped in the window is dropped in the table.
/// </summary>
public interface ICommitTicks
{
    /// <param name="relPath">Relative to the root, as the listing spells it.</param>
    bool IsTicked(string relPath);

    /// <summary>Ticks the path if it is not, unticks it if it is.</summary>
    void Toggle(string relPath);
}
