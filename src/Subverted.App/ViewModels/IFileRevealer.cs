namespace Subverted.App.ViewModels;

/// <summary>Shows a path in the platform's file manager — Explorer, Finder — with it selected.</summary>
public interface IFileRevealer
{
    /// <param name="path">Absolute. It may no longer exist; the nearest folder that does is shown.</param>
    Task RevealAsync(string path);
}
