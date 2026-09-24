using System.Text.Json.Serialization;

namespace Subverted.Core;

/// <summary>How many unchanged lines a diff shows on each side of a change.</summary>
/// <param name="LinesAround">
/// That many lines before and after each change; <see langword="null"/> for the whole file, every
/// unchanged line included. Never negative in a value the daemon acts on — it refuses one.
/// </param>
public sealed record DiffContext(int? LinesAround)
{
    private const int SvnDiffLines = 3;

    /// <summary>The three lines <c>svn diff</c> always prints, and the only amount it can print.</summary>
    public static DiffContext Default { get; } = new(SvnDiffLines);

    /// <summary>Every line of the file, changed or not.</summary>
    public static DiffContext WholeFile { get; } = new((int?)null);

    /// <summary>
    /// More than <see cref="Default"/> — the only amount that needs anything but <c>svn diff</c>.
    /// Fewer than three is not wider, and is answered with three.
    /// </summary>
    [JsonIgnore]
    public bool IsWiderThanDefault => LinesAround is not { } lines || lines > SvnDiffLines;
}
