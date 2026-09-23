using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>
/// Swaps the preset dictionary in the application's merged resources and sets the matching Fluent
/// variant. Every surface is a <c>DynamicResource</c>, so the swap re-resolves without a restart.
/// </summary>
public sealed class ResourceSlotThemeApplier(Application application) : IThemeApplier
{
    private const string PresetFolder = "avares://Subverted/Themes/Presets/";

    public void Apply(ThemePreset preset)
    {
        var merged = application.Resources.MergedDictionaries;
        var include = new ResourceInclude((Uri?)null) { Source = preset.Source };
        var slot = FindPresetSlot(merged);
        if (slot < 0)
        {
            merged.Add(include);
        }
        else
        {
            merged[slot] = include;
        }

        application.RequestedThemeVariant = preset.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static int FindPresetSlot(IList<IResourceProvider> merged)
    {
        for (var index = 0; index < merged.Count; index++)
        {
            if (
                merged[index] is ResourceInclude { Source: { } source }
                && source.OriginalString.StartsWith(PresetFolder, StringComparison.Ordinal)
            )
            {
                return index;
            }
        }

        return -1;
    }
}
