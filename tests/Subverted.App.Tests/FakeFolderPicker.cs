using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

/// <summary>Picks the folder it was given, or cancels when given none.</summary>
internal sealed class FakeFolderPicker(string? picked) : IFolderPicker
{
    public Task<string?> PickAsync(CancellationToken cancellationToken) => Task.FromResult(picked);
}
