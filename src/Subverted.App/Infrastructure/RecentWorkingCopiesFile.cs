using System.Text.Json;
using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>
/// The recent list as a small JSON file in the user's application data. Unreadable is treated as
/// empty rather than as fatal: losing the sidebar's list is a nuisance, refusing to start is not.
/// </summary>
public sealed class RecentWorkingCopiesFile(string path) : IRecentWorkingCopyStore
{
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Subverted",
            "recent-working-copies.json"
        );

    public IReadOnlyList<string> Load()
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, AppJsonContext.Default.StringArray) ?? [];
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Written beside the file and moved over it, so a crash mid-write leaves the old list.</summary>
    public void Save(IReadOnlyList<string> recent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var staging = path + ".new";
        File.WriteAllText(
            staging,
            JsonSerializer.Serialize([.. recent], AppJsonContext.Default.StringArray)
        );
        File.Move(staging, path, overwrite: true);
    }
}
