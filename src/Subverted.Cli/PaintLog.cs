namespace Subverted.Cli;

/// <summary>Wraps one line of a log entry however the terminal will accept it — or does not.</summary>
public delegate string PaintLog(string line, LogLine role);
