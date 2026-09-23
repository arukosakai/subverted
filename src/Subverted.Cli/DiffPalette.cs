namespace Subverted.Cli;

/// <summary>Colour for a unified diff — the one everyone already reads: added green, removed red.</summary>
public static class DiffPalette
{
    private const string Reset = "\e[0m";

    public static PaintDiff Plain { get; } = (line, _) => line;

    public static PaintDiff Ansi { get; } =
        (line, kind) => Colour(kind) is { } colour ? $"{colour}{line}{Reset}" : line;

    private static string? Colour(DiffLine kind) =>
        kind switch
        {
            DiffLine.FileHeader => "\e[1m",
            DiffLine.HunkHeader => "\e[36m",
            DiffLine.Added => "\e[32m",
            DiffLine.Removed => "\e[31m",
            _ => null,
        };
}
