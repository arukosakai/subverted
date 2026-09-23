using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>Which way of revealing a path each platform gets.</summary>
public static class FileRevealers
{
    public static FileManager ThisPlatform =>
        OperatingSystem.IsWindows() ? FileManager.Explorer
        : OperatingSystem.IsMacOS() ? FileManager.Finder
        : FileManager.FreeDesktop;

    public static IFileRevealer For(FileManager fileManager) =>
        fileManager switch
        {
            FileManager.Explorer => new ShellFileRevealer(),
            FileManager.Finder => new ProcessFileRevealer((path, _) => RevealCommand.Finder(path)),
            _ => new ProcessFileRevealer(RevealCommand.FreeDesktop),
        };
}
