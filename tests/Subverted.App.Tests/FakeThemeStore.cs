using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

internal sealed class FakeThemeStore(string? kept) : IThemeChoiceStore
{
    public string? Kept { get; private set; } = kept;

    public int Saves { get; private set; }

    public string? Load() => Kept;

    public void Save(string presetId)
    {
        Saves++;
        Kept = presetId;
    }
}
