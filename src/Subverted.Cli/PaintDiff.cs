namespace Subverted.Cli;

/// <summary>Wraps one line of a diff however the terminal will accept it — or does not.</summary>
public delegate string PaintDiff(string line, DiffLine kind);
