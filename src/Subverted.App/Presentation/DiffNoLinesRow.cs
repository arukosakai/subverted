namespace Subverted.App.Presentation;

/// <summary>
/// A text change SVN printed no hunks for — an empty file added or deleted — said in words rather
/// than left as a blank pane.
/// </summary>
public sealed record DiffNoLinesRow(string Path) : DiffRow;
