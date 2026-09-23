using System.Globalization;

namespace Subverted.App.Presentation;

/// <summary>
/// The line beside the filter that says what it is keeping out of sight, so a filtered list is
/// never mistaken for a working copy with fewer changes in it.
/// </summary>
public static class HiddenChanges
{
    /// <returns><c>null</c> when nothing is hidden, so the line is not there at all.</returns>
    public static string? Text(int listed, int shown)
    {
        var hidden = listed - shown;
        return hidden switch
        {
            <= 0 => null,
            1 => "1 change hidden",
            _ => string.Create(CultureInfo.InvariantCulture, $"{hidden:N0} changes hidden"),
        };
    }
}
