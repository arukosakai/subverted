namespace Subverted.Svn;

/// <summary>
/// A path's BASE revision range through the <c>svnversion</c> binary — D1's fallback for
/// <see cref="BaseRevisionRangeReader"/>, for a wc.db this build does not understand.
/// </summary>
/// <param name="svnversion">A command running <c>svnversion</c>, which ships beside <c>svn</c>.</param>
public sealed class SvnVersionCommand(SvnCommand svnversion)
{
    /// <returns>The range, or null when nothing there has a BASE.</returns>
    /// <exception cref="SvnCommandException">The binary failed, or printed something unreadable.</exception>
    public async Task<Core.BaseRevisionRange?> ReadAsync(
        string workingCopyRoot,
        string path,
        CancellationToken cancellationToken
    )
    {
        var result = await svnversion.RunAsync(
            workingCopyRoot,
            [SvnTarget.WithinForSvnversion(workingCopyRoot, path)],
            cancellationToken
        );

        return result.ExitCode == 0
            ? SvnVersionOutput.Parse(result.StandardOutput)
            : throw new SvnCommandException(result.Complaint);
    }
}
