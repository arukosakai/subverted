using Subverted.Frontend.Diff;

namespace Subverted.App.Presentation;

/// <summary>One line of a file's or a property's text, with both of its line numbers.</summary>
/// <param name="Counterpart">
/// For a changed line, the line of the other kind it is paired with in its run; otherwise <c>null</c>.
/// </param>
public sealed record DiffTextRow(DiffLine Line, DiffLine? Counterpart = null) : DiffRow
{
    /// <summary>The prefix a unified diff gives the line, drawn in its own gutter.</summary>
    public string Sign =>
        Line.Kind switch
        {
            DiffLineKind.Added => "+",
            DiffLineKind.Removed => "−",
            _ => string.Empty,
        };

    /// <summary>What changed within the line against its counterpart, worked out on each read.</summary>
    public IReadOnlyList<ChangedSpan> Changes =>
        (Line, Counterpart) switch
        {
            ({ Kind: DiffLineKind.Removed }, { } added) =>
                IntralineChanges.Between(Line.Text, added.Text).Old,
            ({ Kind: DiffLineKind.Added }, { } removed) =>
                IntralineChanges.Between(removed.Text, Line.Text).New,
            _ => [],
        };
}
