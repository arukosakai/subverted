using System.Diagnostics;
using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>Starts the platform's file manager on a path; see <see cref="RevealCommand"/>.</summary>
public sealed class SystemFileRevealer(FileManager fileManager) : IFileRevealer
{
    public static FileManager ThisPlatform =>
        OperatingSystem.IsWindows() ? FileManager.Explorer
        : OperatingSystem.IsMacOS() ? FileManager.Finder
        : FileManager.FreeDesktop;

    public Task RevealAsync(string path)
    {
        var shown = NearestPresent(path);
        if (shown is null)
        {
            return Task.CompletedTask;
        }

        var command = RevealCommand.For(shown, fileManager, Directory.Exists(shown));
        var start = new ProcessStartInfo(command.FileName) { UseShellExecute = false };
        foreach (var argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // Nothing waits on the file manager; it is the person's window from here on.
        Process.Start(start)?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>A deleted or missing change is not on disk, so the folder it was in is shown instead.</summary>
    /// <returns>The path or its nearest ancestor that exists; <c>null</c> when not even a root does.</returns>
    public static string? NearestPresent(string path)
    {
        for (
            string? candidate = path;
            candidate is not null;
            candidate = Path.GetDirectoryName(candidate)
        )
        {
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
