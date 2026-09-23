using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

internal sealed class FakeFileRevealer : IFileRevealer
{
    public List<string> Revealed { get; } = [];

    public Task RevealAsync(string path)
    {
        Revealed.Add(path);
        return Task.CompletedTask;
    }
}
