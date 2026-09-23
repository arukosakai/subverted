using System.Globalization;
using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// A path's revision history, via <c>svn log --xml</c>. A server round trip every time (D6):
/// nothing here is cached, because a revision the daemon has not heard about is exactly the one
/// the user is asking after.
/// </summary>
public sealed class SvnLogCommand(SvnCommand command)
{
    /// <param name="limit">How many revisions, newest first, or null for all of them.</param>
    /// <exception cref="SvnCommandException">The client failed, or wrote something unreadable.</exception>
    public async Task<IReadOnlyList<RevisionEntry>> ReadAsync(
        string workingCopyRoot,
        string path,
        int? limit,
        CancellationToken cancellationToken
    )
    {
        // --non-interactive matters more here than anywhere else: a daemon sitting on a password
        // prompt answers nothing and says nothing about why.
        List<string> arguments = ["log", "--xml", "--verbose", "--non-interactive"];
        if (limit is { } newest)
        {
            arguments.Add("--limit");
            arguments.Add(newest.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add(SvnTarget.Within(workingCopyRoot, path));

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        return result.ExitCode == 0
            ? SvnLogXml.Parse(result.StandardOutput)
            : throw new SvnCommandException(result.Complaint);
    }
}
