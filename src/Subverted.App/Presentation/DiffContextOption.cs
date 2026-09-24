using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>One entry of the diff pane's context dropdown.</summary>
/// <param name="Label">What the dropdown shows.</param>
/// <param name="Asked">
/// What the daemon is asked for: <see langword="null"/> for SVN's own three lines, which is also
/// what a request without the choice asks.
/// </param>
public sealed record DiffContextOption(string Label, DiffContext? Asked)
{
    /// <summary>SVN's own diff, and where the pane starts.</summary>
    public static DiffContextOption Default { get; } = new("3 lines", null);

    /// <summary>Everything the dropdown offers, in the order it offers them.</summary>
    public static IReadOnlyList<DiffContextOption> All { get; } =
    [
        Default,
        new("10 lines", new DiffContext(10)),
        new("25 lines", new DiffContext(25)),
        new("Whole file", DiffContext.WholeFile),
    ];

    /// <summary>
    /// The answer is not what was asked for: more context was wanted and SVN's own diff came back
    /// instead — because the file cannot have it, or the daemon predates the choice.
    /// </summary>
    /// <param name="given">The <c>DiffResponse.Context</c> the answer came with.</param>
    public bool WasNotHonouredBy(DiffContext? given) => Asked is not null && given != Asked;

    public override string ToString() => Label;
}
