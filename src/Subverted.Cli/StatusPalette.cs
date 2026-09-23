using Subverted.Core;

namespace Subverted.Cli;

/// <summary>
/// Which colour a node is worth. Kept apart from the text so that turning colour off is choosing a
/// different <see cref="Paint"/>, not threading a flag through the renderer.
/// </summary>
/// <remarks>Escapes are spelled <c>\e</c> (C# 13's escape-character escape) rather than embedded as a raw byte.</remarks>
public static class StatusPalette
{
    private const string Reset = "\e[0m";

    /// <summary>Leaves the line exactly as it was. For pipes, dumb terminals and tests.</summary>
    public static Paint Plain { get; } = (line, _) => line;

    public static Paint Ansi { get; } =
        (line, entry) => Colour(entry) is { } colour ? $"{colour}{line}{Reset}" : line;

    private static string? Colour(WorkingCopyEntry entry) =>
        entry.Status switch
        {
            // An obstruction is a broken working copy, not a change — it reads with the conflict.
            NodeStatus.Conflicted or NodeStatus.Obstructed => "\e[1;31m",
            NodeStatus.Deleted or NodeStatus.Missing or NodeStatus.Incomplete => "\e[31m",
            NodeStatus.Replaced => "\e[35m",
            NodeStatus.Added => "\e[32m",
            NodeStatus.Modified or NodeStatus.NeedsPristineCompare => "\e[33m",
            // An external is somebody else's working copy: present, but not this listing's business.
            NodeStatus.Unversioned or NodeStatus.Ignored or NodeStatus.External => "\e[90m",
            // Clean on content but dirty on properties still has work in it (D9), and a line the
            // same colour as an untouched file is a line people stop reading.
            _ => entry.PropertyStatus == PropertyStatus.Modified ? "\e[33m" : null,
        };
}
