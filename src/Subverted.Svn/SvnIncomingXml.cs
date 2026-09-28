using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Reads the server's half of <c>svn status --show-updates --xml</c>: each entry's
/// <c>&lt;repos-status&gt;</c>. Pure, so every shape 1.8.15 was seen to write is a test.
/// </summary>
/// <remarks>
/// The same document lists this copy's own changes and unversioned files, with a repos-status of
/// <c>none</c> or none at all; those are not incoming and are left out. Measured on 1.8.15: a
/// folder is marked <c>modified</c> when a child was added, deleted or replaced, not when a child's
/// content changed, and a folder deleted on the server is listed without its children.
/// </remarks>
public static class SvnIncomingXml
{
    /// <param name="directorySeparator">The separator SVN wrote paths with; see <see cref="SvnStatusXml"/>.</param>
    /// <exception cref="SvnCommandException">
    /// The text is not a status document, says no revision it was compared against, or carries a
    /// server state this build does not model.
    /// </exception>
    public static IncomingChanges Parse(string xml, char directorySeparator)
    {
        XElement status;
        try
        {
            status = XElement.Parse(xml);
        }
        catch (XmlException exception)
        {
            throw new SvnCommandException(
                $"svn status wrote something that is not XML: {exception.Message}",
                exception
            );
        }

        if (status.Name.LocalName != "status")
        {
            throw new SvnCommandException(
                $"svn status wrote a <{status.Name.LocalName}> document, not a <status> one."
            );
        }

        var changes = status
            .Descendants("entry")
            .Select(entry => Incoming(entry, directorySeparator))
            .OfType<IncomingChange>()
            .ToList();

        return new IncomingChanges(AgainstRevision(status), changes);
    }

    private static IncomingChange? Incoming(XElement entry, char directorySeparator)
    {
        if (entry.Element("repos-status") is not { } repository)
        {
            return null;
        }

        var change = Change(Attribute(repository, "item"));
        var propertiesChanged = PropertiesChanged(Attribute(repository, "props"));
        if (change is null && !propertiesChanged)
        {
            return null;
        }

        var path = Attribute(entry, "path");
        var relPath = path == "." ? string.Empty : path.Replace(directorySeparator, '/');
        return new IncomingChange(relPath, change, propertiesChanged);
    }

    private static PathChange? Change(string item) =>
        item switch
        {
            "none" => null,
            "added" => PathChange.Added,
            "deleted" => PathChange.Deleted,
            "modified" => PathChange.Modified,
            "replaced" => PathChange.Replaced,
            _ => throw new SvnCommandException(
                $"svn status reported the incoming state '{item}', which Subverted does not model."
            ),
        };

    private static bool PropertiesChanged(string props) =>
        props switch
        {
            "none" => false,
            "modified" => true,
            _ => throw new SvnCommandException(
                $"svn status reported the incoming property state '{props}', which Subverted does not model."
            ),
        };

    /// <remarks>
    /// Written once per target, and a status of one target has one. Without it the answer is a
    /// list with no moment attached, so it fails rather than guessing HEAD.
    /// </remarks>
    private static long AgainstRevision(XElement status)
    {
        var against =
            status.Descendants("against").FirstOrDefault()
            ?? throw new SvnCommandException(
                "svn status wrote no <against> revision, so it did not ask the server."
            );
        var revision = Attribute(against, "revision");

        return long.TryParse(revision, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new SvnCommandException(
                $"svn status wrote against/@revision as '{revision}', which is not a revision number."
            );
    }

    private static string Attribute(XElement element, string name) =>
        (string?)element.Attribute(name)
        ?? throw new SvnCommandException(
            $"svn status wrote a <{element.Name.LocalName}> with no {name} attribute."
        );
}
