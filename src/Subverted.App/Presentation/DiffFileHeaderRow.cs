namespace Subverted.App.Presentation;

/// <summary>Names the file the rows beneath it belong to, when one diff covers several files.</summary>
/// <param name="Path">As the diff names it; empty for the diffed folder's own section.</param>
public sealed record DiffFileHeaderRow(string Path) : DiffRow
{
    public string Title => Path.Length == 0 ? "This folder" : Path;

    public override string AutomationName => Title;
}
