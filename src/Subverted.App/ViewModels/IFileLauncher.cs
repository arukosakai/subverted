namespace Subverted.App.ViewModels;

/// <summary>Hands a file to whatever the operating system opens it with.</summary>
public interface IFileLauncher
{
    Task OpenAsync(string path);
}
