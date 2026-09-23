using Subverted.Core;

namespace Subverted.Cli;

/// <summary>Wraps a rendered line however the terminal will accept it — or does not.</summary>
public delegate string Paint(string line, WorkingCopyEntry entry);
