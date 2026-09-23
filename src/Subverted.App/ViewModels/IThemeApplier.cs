using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>Puts a preset on screen, replacing whichever one was showing.</summary>
public interface IThemeApplier
{
    void Apply(ThemePreset preset);
}
