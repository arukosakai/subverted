namespace Subverted.App.Presentation;

/// <summary>Names the file the rows beneath it belong to, when one diff covers several files.</summary>
public sealed record DiffFileHeaderRow(string Path) : DiffRow;
