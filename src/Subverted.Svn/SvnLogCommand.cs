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
    /// <param name="start">The newest revision to list, or null for the path's BASE, as <c>svn log</c> does.</param>
    /// <exception cref="SvnCommandException">The client failed, or wrote something unreadable.</exception>
    public async Task<IReadOnlyList<RevisionEntry>> ReadAsync(
        string workingCopyRoot,
        string path,
        int? limit,
        HistoryStart? start,
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

        if (RangeFrom(start) is { } range)
        {
            arguments.Add("--revision");
            arguments.Add(range);
        }

        arguments.Add(SvnTarget.Within(workingCopyRoot, path));

        var result = await command.RunAsync(workingCopyRoot, arguments, cancellationToken);
        return result.ExitCode == 0
            ? SvnLogXml.Parse(result.StandardOutput)
            : throw new SvnCommandException(result.Complaint);
    }

    /// <remarks>
    /// Measured on 1.8.15: <c>-r HEAD:1</c> on a working-copy path lists revisions past the path's
    /// BASE, tracing the node forward from where it is checked out.
    /// </remarks>
    private static string? RangeFrom(HistoryStart? start) =>
        start switch
        {
            null => null,
            HistoryFromHead => "HEAD:1",
            HistoryFromRevision from => from.Revision.ToString(CultureInfo.InvariantCulture) + ":1",
            _ => throw new ArgumentOutOfRangeException(nameof(start), start, null),
        };
}
