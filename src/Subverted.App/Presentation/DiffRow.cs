using Subverted.Frontend.Diff;

namespace Subverted.App.Presentation;

/// <summary>
/// One row of a flattened diff, as the virtualised line list draws it. A closed hierarchy: the
/// <c>Diff*Row</c> types beside this file are the only kinds of row there are.
/// </summary>
public abstract record DiffRow
{
    private protected DiffRow() { }

    /// <summary>What a screen reader says for the row: what is on it, without the drawing.</summary>
    public abstract string AutomationName { get; }

    /// <summary>A line as it is heard: which side it is on, its number there, and its text.</summary>
    private protected static string Spoken(DiffLine line) =>
        line.Kind switch
        {
            DiffLineKind.Added => $"Added line {line.NewNumber}: {line.Text}",
            DiffLineKind.Removed => $"Removed line {line.OldNumber}: {line.Text}",
            _ => $"Line {line.NewNumber}: {line.Text}",
        };
}
