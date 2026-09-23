using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Subverted.App.Infrastructure;
using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class ThemeInfrastructureTests
{
    [Test]
    public async Task A_kept_choice_reads_back_as_written()
    {
        using var folder = new ScratchFolder();
        var path = Path.Combine(folder.Path, "theme.txt");

        new ThemeChoiceFile(path).Save("tokyo-night");

        await Assert.That(new ThemeChoiceFile(path).Load()).IsEqualTo("tokyo-night");
        await Assert.That(File.Exists(path + ".new")).IsFalse();
    }

    [Test]
    public async Task Saving_again_replaces_the_earlier_choice()
    {
        using var folder = new ScratchFolder();
        var file = new ThemeChoiceFile(Path.Combine(folder.Path, "theme.txt"));

        file.Save("nord");
        file.Save("gruvbox");

        await Assert.That(file.Load()).IsEqualTo("gruvbox");
    }

    [Test]
    public async Task No_file_yet_is_nothing_kept()
    {
        using var folder = new ScratchFolder();

        await Assert
            .That(new ThemeChoiceFile(Path.Combine(folder.Path, "none.txt")).Load())
            .IsNull();
    }

    /// <summary>A hand-edited file keeps working through the stray newline an editor leaves.</summary>
    [Test]
    [Arguments("nord\n", "nord")]
    [Arguments("  nord  \r\n", "nord")]
    public async Task Surrounding_whitespace_is_not_part_of_the_id(string written, string expected)
    {
        using var folder = new ScratchFolder();
        var path = Path.Combine(folder.Path, "theme.txt");
        File.WriteAllText(path, written);

        await Assert.That(new ThemeChoiceFile(path).Load()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments(" \n")]
    public async Task An_empty_file_is_nothing_kept(string written)
    {
        using var folder = new ScratchFolder();
        var path = Path.Combine(folder.Path, "theme.txt");
        File.WriteAllText(path, written);

        await Assert.That(new ThemeChoiceFile(path).Load()).IsNull();
    }

    /// <summary>A directory where the file should be cannot be read, and must not stop the app starting.</summary>
    [Test]
    public async Task An_unreadable_path_is_nothing_kept()
    {
        using var folder = new ScratchFolder();

        await Assert.That(new ThemeChoiceFile(folder.Path).Load()).IsNull();
    }

    [Test]
    public async Task Applying_replaces_the_preset_slot_rather_than_stacking_another()
    {
        var (count, background) = await HeadlessApp.Session.Dispatch(
            () =>
            {
                var application = ApplicationWith(ThemeCatalog.DefaultDark);
                new ResourceSlotThemeApplier(application).Apply(Nord);
                return (
                    application.Resources.MergedDictionaries.Count,
                    ColorOf(application, "BgColor")
                );
            },
            CancellationToken.None
        );

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(background).IsEqualTo(Color.Parse(Nord.Background));
    }

    [Test]
    public async Task Applying_with_no_preset_slot_yet_adds_one()
    {
        var (count, background) = await HeadlessApp.Session.Dispatch(
            () =>
            {
                var application = ApplicationWith();
                new ResourceSlotThemeApplier(application).Apply(Nord);
                return (
                    application.Resources.MergedDictionaries.Count,
                    ColorOf(application, "BgColor")
                );
            },
            CancellationToken.None
        );

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(background).IsEqualTo(Color.Parse(Nord.Background));
    }

    [Test]
    public async Task Other_dictionaries_are_left_where_they_are()
    {
        var first = await HeadlessApp.Session.Dispatch(
            () =>
            {
                var application = ApplicationWith(ThemeCatalog.DefaultDark);
                new ResourceSlotThemeApplier(application).Apply(Nord);
                return ((ResourceInclude)application.Resources.MergedDictionaries[0])
                    .Source!
                    .OriginalString;
            },
            CancellationToken.None
        );

        await Assert.That(first).IsEqualTo(Tokens.OriginalString);
    }

    [Test]
    [Arguments("default-light", "Light")]
    [Arguments("claude", "Light")]
    [Arguments("nord", "Dark")]
    public async Task Fluent_is_put_on_the_presets_own_variant(string id, string expected)
    {
        var variant = await HeadlessApp.Session.Dispatch(
            () =>
            {
                var application = ApplicationWith(ThemeCatalog.DefaultDark);
                application.RequestedThemeVariant = ThemeVariant.Default;
                new ResourceSlotThemeApplier(application).Apply(
                    ThemeCatalog.All.Single(preset => preset.Id == id)
                );
                return application.RequestedThemeVariant?.Key.ToString();
            },
            CancellationToken.None
        );

        await Assert.That(variant).IsEqualTo(expected);
    }

    private static readonly Uri Tokens = new("avares://Subverted/Themes/Tokens.axaml");

    private static ThemePreset Nord => ThemeCatalog.All.Single(preset => preset.Id == "nord");

    /// <summary>A fresh application, so a swap here never changes the palette other tests render in.</summary>
    private static Application ApplicationWith(params ThemePreset[] presets)
    {
        var application = new Application();
        application.Resources.MergedDictionaries.Add(
            new ResourceInclude((Uri?)null) { Source = Tokens }
        );
        foreach (var preset in presets)
        {
            application.Resources.MergedDictionaries.Add(
                new ResourceInclude((Uri?)null) { Source = preset.Source }
            );
        }

        return application;
    }

    private static Color? ColorOf(Application application, string key) =>
        application.Resources.TryGetResource(key, null, out var value) ? (Color?)value : null;

    private sealed class ScratchFolder : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"subverted-theme-{Guid.NewGuid():N}"
            );

        public ScratchFolder() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
