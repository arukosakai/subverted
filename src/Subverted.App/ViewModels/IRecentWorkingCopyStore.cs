namespace Subverted.App.ViewModels;

/// <summary>Where the sidebar's list is kept between runs.</summary>
public interface IRecentWorkingCopyStore
{
    /// <returns>Most recent first; empty on first run or when what was kept cannot be read.</returns>
    IReadOnlyList<string> Load();

    void Save(IReadOnlyList<string> recent);
}
