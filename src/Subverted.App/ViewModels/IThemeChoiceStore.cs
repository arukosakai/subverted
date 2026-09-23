namespace Subverted.App.ViewModels;

/// <summary>Where the chosen theme is kept between runs.</summary>
public interface IThemeChoiceStore
{
    /// <returns>The kept preset's id; null on first run or when what was kept cannot be read.</returns>
    string? Load();

    void Save(string presetId);
}
