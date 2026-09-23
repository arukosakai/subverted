using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

/// <summary>Knows the sizes it was told, and nothing else is on disk.</summary>
internal sealed class FakeFileSizeReader(params (string Path, long Size)[] files) : IFileSizeReader
{
    public List<string> Asked { get; } = [];

    public long? ReadSize(string path)
    {
        Asked.Add(path);
        return files
            .Where(file => file.Path == path)
            .Select(file => (long?)file.Size)
            .FirstOrDefault();
    }
}
