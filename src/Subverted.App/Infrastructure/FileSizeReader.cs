using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>The real disk behind <see cref="IFileSizeReader"/>, and nothing else.</summary>
public sealed class FileSizeReader : IFileSizeReader
{
    public long? ReadSize(string path) =>
        new FileInfo(path) is { Exists: true } file ? file.Length : null;
}
