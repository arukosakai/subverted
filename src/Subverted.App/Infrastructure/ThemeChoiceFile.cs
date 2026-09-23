using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>
/// The chosen preset's id as one line of text in the user's application data. Unreadable reads as
/// nothing kept: the app then starts in the system's light or dark, which is a fine answer.
/// </summary>
public sealed class ThemeChoiceFile(string path) : IThemeChoiceStore
{
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Subverted",
            "theme.txt"
        );

    public string? Load()
    {
        try
        {
            var kept = File.ReadAllText(path).Trim();
            return kept.Length == 0 ? null : kept;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Written beside the file and moved over it, so a crash mid-write leaves the old choice.</summary>
    public void Save(string presetId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var staging = path + ".new";
        File.WriteAllText(staging, presetId);
        File.Move(staging, path, overwrite: true);
    }
}
