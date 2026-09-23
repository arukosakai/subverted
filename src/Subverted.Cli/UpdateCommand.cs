namespace Subverted.Cli;

/// <param name="Path">
/// Absolute, already resolved against the working directory; the current directory when none was
/// given. Unlike the commands that write, a bare <c>sv up</c> is the thing people mean to type —
/// an update takes nothing away, it brings the rest of the studio's work in.
/// </param>
public sealed record UpdateCommand(string Path) : CliCommand;
