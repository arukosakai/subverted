using System.Globalization;

namespace Subverted.App.Presentation;

/// <summary>What the Update button says about the server, once it has been asked.</summary>
public static class IncomingText
{
    /// <returns>Null when nothing is waiting, so the button carries no badge.</returns>
    public static string? Badge(int? incoming) =>
        incoming is { } count && count > 0
            ? count.ToString("N0", CultureInfo.InvariantCulture)
            : null;

    /// <returns>A sentence for the tooltip and a screen reader; null when the server was not reached.</returns>
    public static string? Sentence(int? incoming) =>
        incoming switch
        {
            null => null,
            0 => "Nothing new on the server",
            1 => "1 change on the server to bring in",
            var count => string.Create(
                CultureInfo.InvariantCulture,
                $"{count:N0} changes on the server to bring in"
            ),
        };
}
