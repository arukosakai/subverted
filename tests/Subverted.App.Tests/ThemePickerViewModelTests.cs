using Subverted.App.Presentation;
using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

public sealed class ThemePickerViewModelTests
{
    [Test]
    public async Task Starting_applies_the_kept_preset_without_keeping_it_again()
    {
        var store = new FakeThemeStore("gruvbox");
        var applier = new FakeThemeApplier();

        var picker = new ThemePickerViewModel(store, applier, systemPrefersDark: false);

        await Assert.That(picker.Selected.Id).IsEqualTo("gruvbox");
        await Assert.That(string.Join(",", applier.Applied)).IsEqualTo("gruvbox");
        await Assert.That(store.Saves).IsEqualTo(0);
    }

    [Test]
    [Arguments(true, "default-dark")]
    [Arguments(false, "default-light")]
    public async Task A_first_run_starts_in_the_systems_light_or_dark(
        bool systemPrefersDark,
        string expected
    )
    {
        var applier = new FakeThemeApplier();

        var picker = new ThemePickerViewModel(new FakeThemeStore(null), applier, systemPrefersDark);

        await Assert.That(picker.Selected.Id).IsEqualTo(expected);
        await Assert.That(string.Join(",", applier.Applied)).IsEqualTo(expected);
    }

    [Test]
    public async Task Choosing_a_preset_shows_it_and_keeps_it()
    {
        var store = new FakeThemeStore(null);
        var applier = new FakeThemeApplier();
        var picker = new ThemePickerViewModel(store, applier, systemPrefersDark: true);
        var nord = picker.Presets.Single(preset => preset.Id == "nord");
        var announced = new List<string?>();
        picker.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        picker.Selected = nord;

        await Assert.That(string.Join(",", applier.Applied)).IsEqualTo("default-dark,nord");
        await Assert.That(store.Kept).IsEqualTo("nord");
        await Assert.That(store.Saves).IsEqualTo(1);
        await Assert.That(announced).Contains(nameof(ThemePickerViewModel.Selected));
    }

    [Test]
    public async Task Choosing_the_preset_already_showing_neither_reapplies_nor_writes()
    {
        var store = new FakeThemeStore("nord");
        var applier = new FakeThemeApplier();
        var picker = new ThemePickerViewModel(store, applier, systemPrefersDark: true);

        picker.Selected = ThemeCatalog.All.Single(preset => preset.Id == "nord");

        await Assert.That(applier.Applied.Count).IsEqualTo(1);
        await Assert.That(store.Saves).IsEqualTo(0);
    }

    [Test]
    public async Task The_picker_offers_every_bundled_preset_in_catalog_order()
    {
        var picker = new ThemePickerViewModel(
            new FakeThemeStore(null),
            new FakeThemeApplier(),
            systemPrefersDark: true
        );

        await Assert.That(picker.Presets).IsEquivalentTo(ThemeCatalog.All);
        await Assert.That(picker.Presets[0]).IsEqualTo(ThemeCatalog.DefaultDark);
    }
}
