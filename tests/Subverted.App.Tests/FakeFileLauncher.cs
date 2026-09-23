using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

internal sealed class FakeFileLauncher : IFileLauncher
{
    public List<string> Opened { get; } = [];

    public Task OpenAsync(string path)
    {
        Opened.Add(path);
        return Task.CompletedTask;
    }
}
