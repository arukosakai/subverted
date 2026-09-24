using Subverted.Frontend.Diff;

namespace Subverted.App.Presentation;

/// <summary>
/// One row of a side-by-side diff: the old line on the left, the new line on the right. A context
/// line is the same line on both sides; a side with nothing to pair is <c>null</c> and drawn blank.
/// </summary>
/// <param name="Old">A context or removed line, or <c>null</c> for filler.</param>
/// <param name="New">A context or added line, or <c>null</c> for filler.</param>
public sealed record DiffSplitRow(DiffLine? Old, DiffLine? New) : DiffRow
{
    public bool IsContext => Old is { Kind: DiffLineKind.Context };

    public string OldSign => Old is { Kind: DiffLineKind.Removed } ? "−" : string.Empty;

    public string NewSign => New is { Kind: DiffLineKind.Added } ? "+" : string.Empty;
}
