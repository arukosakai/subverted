using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// What working copy a path belongs to, via <c>svn info --xml</c>. The fallback asks this before it
/// can ask anything else: it needs the root to run <c>svn status</c> in.
/// </summary>
public sealed class SvnInfoCommand(SvnCommand command)
{
    /// <param name="path">
    /// Any path inside the working copy. A file is asked about from its own directory, because the
    /// client is run in a directory and the answer is about the copy, not the path.
    /// </param>
    /// <exception cref="SvnCommandException">
    /// The client failed — including because there is no working copy here — or wrote something
    /// unreadable.
    /// </exception>
    public async Task<WorkingCopyInfo> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;

        var result = await command.RunAsync(
            directory,
            ["info", "--xml", "--non-interactive", "."],
            cancellationToken
        );

        if (result.ExitCode != 0)
        {
            throw new SvnCommandException(result.Complaint);
        }

        // svn writes wcroot-abspath with forward slashes even on Windows, and the daemon compares
        // this root against request paths that come from the platform.
        var info = SvnInfoXml.Parse(result.StandardOutput);
        return info with { RootPath = Path.GetFullPath(info.RootPath) };
    }
}
