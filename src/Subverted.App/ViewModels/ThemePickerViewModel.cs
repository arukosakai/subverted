using CommunityToolkit.Mvvm.ComponentModel;
using Subverted.App.Presentation;

namespace Subverted.App.ViewModels;

/// <summary>
/// The theme setting. Constructing it applies the kept preset, so it belongs in the composition
/// root before the window opens and the first frame is already in the right palette.
/// </summary>
public sealed class ThemePickerViewModel : ObservableObject
{
    private readonly IThemeChoiceStore _store;
    private readonly IThemeApplier _applier;
    private ThemePreset _selected;

    public ThemePickerViewModel(
        IThemeChoiceStore store,
        IThemeApplier applier,
        bool systemPrefersDark
    )
    {
        _store = store;
        _applier = applier;
        _selected = ThemeCatalog.Resolve(store.Load(), systemPrefersDark);
        applier.Apply(_selected);
    }

    public IReadOnlyList<ThemePreset> Presets => ThemeCatalog.All;

    /// <summary>Choosing one shows it at once and keeps it; choosing the one showing does neither.</summary>
    public ThemePreset Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
            {
                return;
            }

            _applier.Apply(value);
            _store.Save(value.Id);
        }
    }
}
