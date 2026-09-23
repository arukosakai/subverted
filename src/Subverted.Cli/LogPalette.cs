namespace Subverted.Cli;

/// <summary>
/// Colour for a revision listing. Same shape as <see cref="StatusPalette"/>: turning colour off is
/// choosing a different <see cref="PaintLog"/>, not threading a flag through the renderer.
/// </summary>
public static class LogPalette
{
    private const string Reset = "\e[0m";

    public static PaintLog Plain { get; } = (line, _) => line;

    public static PaintLog Ansi { get; } =
        (line, kind) => Colour(kind) is { } colour ? $"{colour}{line}{Reset}" : line;

    private static string? Colour(LogLine kind) =>
        kind switch
        {
            LogLine.Heading => "\e[33m",
            LogLine.Path => "\e[90m",
            _ => null,
        };
}
