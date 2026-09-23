namespace Subverted.App.Infrastructure;

/// <summary>Which file manager the platform has, as far as showing a path in it goes.</summary>
public enum FileManager
{
    Explorer,
    Finder,

    /// <summary>Anything reached through <c>xdg-open</c>, which opens a folder but cannot select in it.</summary>
    FreeDesktop,
}
