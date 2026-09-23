using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class ThemeCatalogTests
{
    /// <summary>Every key a view or style looks up. A preset missing one paints that surface transparent.</summary>
    private static readonly string[] SurfaceKeys =
    [
        "BgBrush",
        "SurfaceBrush",
        "RaisedBrush",
        "RailBrush",
        "LineBrush",
        "TextBrush",
        "MutedBrush",
        "FaintBrush",
        "AccentBrush",
        "AccentHoverBrush",
        "AccentPressedBrush",
        "OnAccentBrush",
        "HoverBrush",
        "SelectedBrush",
        "WindowFallbackBrush",
        "SidebarBrush",
        "PanelBrush",
        "DividerBrush",
        "GlassEdgeBrush",
        "SystemAccentColor",
        "SystemAccentColorLight1",
        "SystemAccentColorDark1",
    ];

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_kept_preset_wins_over_the_systems_light_or_dark(bool systemPrefersDark)
    {
        var preset = ThemeCatalog.Resolve("nord", systemPrefersDark);

        await Assert.That(preset.Id).IsEqualTo("nord");
    }

    [Test]
    [Arguments(null, true, "default-dark")]
    [Arguments(null, false, "default-light")]
    [Arguments("a-preset-since-removed", true, "default-dark")]
    [Arguments("a-preset-since-removed", false, "default-light")]
    public async Task With_nothing_usable_kept_the_default_follows_the_system(
        string? kept,
        bool systemPrefersDark,
        string expected
    )
    {
        var preset = ThemeCatalog.Resolve(kept, systemPrefersDark);

        await Assert.That(preset.Id).IsEqualTo(expected);
    }

    /// <summary>A light preset kept on a dark system is still the light preset: the choice is the person's.</summary>
    [Test]
    public async Task A_kept_light_preset_stays_light_on_a_dark_system()
    {
        var preset = ThemeCatalog.Resolve("claude", systemPrefersDark: true);

        await Assert.That(preset.IsDark).IsFalse();
        await Assert.That(preset.Id).IsEqualTo("claude");
    }

    [Test]
    public async Task Ids_are_unique_because_they_are_what_is_kept()
    {
        var ids = ThemeCatalog.All.Select(preset => preset.Id).ToArray();

        await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Length);
    }

    [Test]
    public async Task The_defaults_are_listed_first_dark_then_light()
    {
        await Assert.That(ThemeCatalog.All[0]).IsEqualTo(ThemeCatalog.DefaultDark);
        await Assert.That(ThemeCatalog.All[1]).IsEqualTo(ThemeCatalog.DefaultLight);
    }

    [Test]
    public async Task A_preset_lives_under_the_presets_folder_by_its_file_name()
    {
        await Assert
            .That(ThemeCatalog.DefaultDark.Source.OriginalString)
            .IsEqualTo("avares://Subverted/Themes/Presets/DefaultDark.axaml");
    }

    [Test]
    public async Task Every_preset_defines_every_surface_the_views_look_up()
    {
        var missing = await HeadlessApp.Session.Dispatch(
            () =>
                ThemeCatalog
                    .All.SelectMany(preset =>
                        SurfaceKeys
                            .Where(key => !Load(preset).TryGetResource(key, null, out _))
                            .Select(key => $"{preset.Id}: {key}")
                    )
                    .ToArray(),
            CancellationToken.None
        );

        await Assert.That(missing).IsEmpty();
    }

    /// <summary>The swatch is repeated in code for the picker; it must not drift from the dictionary.</summary>
    [Test]
    public async Task Every_swatch_matches_the_colours_its_dictionary_paints()
    {
        var drifted = await HeadlessApp.Session.Dispatch(
            () =>
                ThemeCatalog
                    .All.SelectMany(preset =>
                        new[]
                        {
                            ("BgColor", preset.Background),
                            ("SurfaceColor", preset.Surface),
                            ("AccentColor", preset.Accent),
                        }
                            .Where(pair =>
                                ColorOf(Load(preset), pair.Item1) != Color.Parse(pair.Item2)
                            )
                            .Select(pair => $"{preset.Id}: {pair.Item1}")
                    )
                    .ToArray(),
            CancellationToken.None
        );

        await Assert.That(drifted).IsEmpty();
    }

    private static ResourceInclude Load(ThemePreset preset) =>
        new((Uri?)null) { Source = preset.Source };

    private static Color? ColorOf(IResourceNode dictionary, string key) =>
        dictionary.TryGetResource(key, null, out var value) ? (Color?)value : null;
}
