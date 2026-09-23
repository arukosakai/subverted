namespace Subverted.App.ViewModels;

/// <summary>What the binary card needs from the disk: how big the file is now.</summary>
public interface IFileSizeReader
{
    /// <returns>The size in bytes, or <c>null</c> when no file is there — missing, or a folder.</returns>
    long? ReadSize(string path);
}
