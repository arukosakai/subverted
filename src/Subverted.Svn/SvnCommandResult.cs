namespace Subverted.Svn;

/// <param name="StandardError">
/// Where <c>svn</c> puts its diagnostics. Not empty on success either — a checkout can warn while
/// still doing what was asked.
/// </param>
public sealed record SvnCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// What to tell the user when <see cref="ExitCode"/> is not zero. SVN's own wording is better
    /// than anything we would write over it, so it is passed through whenever there is any.
    /// </summary>
    public string Complaint =>
        StandardError.Trim() is { Length: > 0 } reported
            ? reported
            : $"svn exited with code {ExitCode} and said nothing about why.";
}
