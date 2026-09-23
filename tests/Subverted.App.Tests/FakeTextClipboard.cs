using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

internal sealed class FakeTextClipboard : ITextClipboard
{
    public List<string> Copied { get; } = [];

    public Task CopyAsync(string text)
    {
        Copied.Add(text);
        return Task.CompletedTask;
    }
}
