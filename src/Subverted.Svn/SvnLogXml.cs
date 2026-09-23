using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Reads the document <c>svn log --xml</c> writes. Pure: hand it the text and it hands back
/// revisions, so every shape SVN can emit is a test rather than a repository.
/// </summary>
public static class SvnLogXml
{
    /// <returns>Revisions in the order SVN listed them, which is newest first.</returns>
    /// <exception cref="SvnCommandException">
    /// The text is not a log document, or carries a revision number or action this build cannot
    /// read. An unreadable revision fails the whole call rather than being skipped — a log with a
    /// silent hole in it is worse than no log.
    /// </exception>
    public static IReadOnlyList<RevisionEntry> Parse(string xml)
    {
        // XElement rather than XDocument: it returns the root element itself, so there is no
        // "parsed but has no root" case to handle that nothing can actually produce.
        XElement log;
        try
        {
            log = XElement.Parse(xml);
        }
        catch (XmlException exception)
        {
            throw new SvnCommandException(
                $"svn log wrote something that is not XML: {exception.Message}",
                exception
            );
        }

        if (log.Name.LocalName != "log")
        {
            throw new SvnCommandException(
                $"svn log wrote a <{log.Name.LocalName}> document, not a <log> one."
            );
        }

        return [.. log.Elements("logentry").Select(Revision)];
    }

    private static RevisionEntry Revision(XElement entry) =>
        new(
            Number(entry.Attribute("revision")?.Value, "logentry/@revision"),
            (string?)entry.Element("author"),
            Moment((string?)entry.Element("date")),
            (string?)entry.Element("msg") ?? string.Empty,
            [.. entry.Elements("paths").Elements("path").Select(Changed)]
        );

    private static ChangedPath Changed(XElement path) =>
        new(
            path.Value,
            Change(path.Attribute("action")?.Value),
            (string?)path.Attribute("copyfrom-path"),
            path.Attribute("copyfrom-rev") is { } from
                ? Number(from.Value, "path/@copyfrom-rev")
                : null
        );

    private static PathChange Change(string? action) =>
        action switch
        {
            "A" => PathChange.Added,
            "D" => PathChange.Deleted,
            "M" => PathChange.Modified,
            "R" => PathChange.Replaced,
            _ => throw new SvnCommandException(
                $"svn log reported the action '{action}', which this build does not know."
            ),
        };

    /// <summary>
    /// A missing or unreadable date costs the date, not the revision — the revision number and
    /// the message are the parts nobody can do without.
    /// </summary>
    private static DateTimeOffset? Moment(string? text) =>
        DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed
        )
            ? parsed
            : null;

    private static long Number(string? text, string what) =>
        long.TryParse(text, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new SvnCommandException(
                $"svn log wrote {what} as '{text}', which is not a revision number."
            );
}
