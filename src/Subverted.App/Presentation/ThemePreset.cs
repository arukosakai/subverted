namespace Subverted.App.Presentation;

/// <summary>
/// One bundled palette: the dictionary under <c>Themes/Presets</c> that holds its surfaces, and
/// whether it sits on Fluent's dark or light variant, which also picks the status tones.
/// </summary>
/// <param name="Id">Stable, and what is kept between runs; the display name is free to change.</param>
/// <param name="FileName">The dictionary's file name under <c>Themes/Presets</c>, without extension.</param>
/// <param name="Background">The preset's own background, repeated here so a picker can draw a swatch.</param>
/// <param name="Surface">Its panel surface, for the swatch.</param>
/// <param name="Accent">Its accent, for the swatch.</param>
public sealed record ThemePreset(
    string Id,
    string DisplayName,
    bool IsDark,
    string FileName,
    string Background,
    string Surface,
    string Accent
)
{
    public Uri Source => new($"avares://Subverted/Themes/Presets/{FileName}.axaml");
}
