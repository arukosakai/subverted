using Subverted.Frontend.Diff;

namespace Subverted.App.Presentation;

/// <summary>One line of a file's or a property's text, with both of its line numbers.</summary>
public sealed record DiffTextRow(DiffLine Line) : DiffRow
{
    /// <summary>The prefix a unified diff gives the line, drawn in its own gutter.</summary>
    public string Sign =>
        Line.Kind switch
        {
            DiffLineKind.Added => "+",
            DiffLineKind.Removed => "−",
            _ => string.Empty,
        };
}
