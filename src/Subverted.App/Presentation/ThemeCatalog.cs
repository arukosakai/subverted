namespace Subverted.App.Presentation;

/// <summary>The palettes the app ships with, in picker order, and which one a run starts in.</summary>
public static class ThemeCatalog
{
    public static readonly ThemePreset DefaultDark = new(
        "default-dark",
        "Default Dark",
        true,
        "DefaultDark",
        "#1F2024",
        "#2A2C31",
        "#7C5CFF"
    );

    public static readonly ThemePreset DefaultLight = new(
        "default-light",
        "Default Light",
        false,
        "DefaultLight",
        "#F7F7F9",
        "#FFFFFF",
        "#7C5CFF"
    );

    public static readonly IReadOnlyList<ThemePreset> All =
    [
        DefaultDark,
        DefaultLight,
        new("nord", "Nord", true, "Nord", "#2E3440", "#3B4252", "#88C0D0"),
        new("tokyo-night", "Tokyo Night", true, "TokyoNight", "#1A1B26", "#24283B", "#7AA2F7"),
        new(
            "solarized-dark",
            "Solarized Dark",
            true,
            "SolarizedDark",
            "#002B36",
            "#073642",
            "#268BD2"
        ),
        new("gruvbox", "Gruvbox Dark", true, "Gruvbox", "#282828", "#3C3836", "#FE8019"),
        new("claude", "Claude", false, "Claude", "#F5F0E8", "#FAF6EF", "#C96442"),
        new("spotify", "Spotify", true, "Spotify", "#121212", "#181818", "#1DB954"),
        new("apple-music", "Apple Music", true, "AppleMusic", "#0A0A0A", "#1A1A1A", "#FA243C"),
        new("tidal", "Tidal", true, "Tidal", "#000000", "#0E0E10", "#00FFFF"),
        new("deezer", "Deezer", true, "Deezer", "#13121A", "#1E1D29", "#EF5466"),
    ];

    /// <summary>
    /// The kept preset if it is still one this build ships; otherwise the default that matches the
    /// system's light or dark, so a first run, or a preset since removed, still looks native.
    /// </summary>
    public static ThemePreset Resolve(string? keptId, bool systemPrefersDark) =>
        All.FirstOrDefault(preset => preset.Id == keptId)
        ?? (systemPrefersDark ? DefaultDark : DefaultLight);
}
