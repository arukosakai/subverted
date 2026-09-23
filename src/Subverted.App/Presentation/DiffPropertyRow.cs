using Subverted.Frontend.Diff;

namespace Subverted.App.Presentation;

/// <summary>One changed property's name and what happened to it; its lines follow as text rows.</summary>
public sealed record DiffPropertyRow(string Name, PropertyChangeKind Kind) : DiffRow
{
    public string Label => Kind.ToString();

    public ChangeTone Tone =>
        Kind switch
        {
            PropertyChangeKind.Added => ChangeTone.Added,
            PropertyChangeKind.Modified => ChangeTone.Modified,
            PropertyChangeKind.Deleted => ChangeTone.Deleted,
            _ => ChangeTone.Quiet,
        };
}
