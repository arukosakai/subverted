using Subverted.App.Presentation;
using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

internal sealed class FakeThemeApplier : IThemeApplier
{
    public List<string> Applied { get; } = [];

    public void Apply(ThemePreset preset) => Applied.Add(preset.Id);
}
