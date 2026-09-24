namespace Subverted.App.Presentation;

/// <summary>A run of a line's text that changed, in UTF-16 code units from the start of the line.</summary>
public readonly record struct ChangedSpan(int Start, int Length)
{
    public int End => Start + Length;
}
