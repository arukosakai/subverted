using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

internal sealed class FakeRecentStore(params string[] kept) : IRecentWorkingCopyStore
{
    public IReadOnlyList<string> Kept { get; private set; } = kept;

    public int Saves { get; private set; }

    public IReadOnlyList<string> Load() => Kept;

    public void Save(IReadOnlyList<string> recent)
    {
        Saves++;
        Kept = recent;
    }
}
