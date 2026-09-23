using System.Xml;
using System.Xml.Linq;
using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Reads the document <c>svn info --xml</c> writes. What the fallback uses in place of the three
/// things wc.db would have told it about the working copy itself.
/// </summary>
public static class SvnInfoXml
{
    /// <returns>
    /// The working copy the entry describes, with a null <see cref="WorkingCopyInfo.Format"/>: the
    /// client does not report wc.db's schema version, and a number invented here would defeat the
    /// version gate that made the fallback necessary.
    /// </returns>
    /// <exception cref="SvnCommandException">
    /// Not an info document, or one missing a part the daemon needs.
    /// </exception>
    public static WorkingCopyInfo Parse(string xml)
    {
        XElement info;
        try
        {
            info = XElement.Parse(xml);
        }
        catch (XmlException exception)
        {
            throw new SvnCommandException(
                $"svn info wrote something that is not XML: {exception.Message}",
                exception
            );
        }

        if (info.Name.LocalName != "info")
        {
            throw new SvnCommandException(
                $"svn info wrote a <{info.Name.LocalName}> document, not an <info> one."
            );
        }

        var entry =
            info.Element("entry")
            ?? throw new SvnCommandException("svn info wrote no <entry> to read.");
        // Required once here rather than conditionally on each child: with `repository?.Element`
        // twice, the second null check could never run — the first would already have thrown.
        var repository =
            entry.Element("repository")
            ?? throw new SvnCommandException("svn info wrote no <repository>.");

        return new WorkingCopyInfo(
            RootPath: Required(
                entry.Element("wc-info")?.Element("wcroot-abspath"),
                "wc-info/wcroot-abspath"
            ),
            RepositoryRoot: Required(repository.Element("root"), "repository/root"),
            RepositoryUuid: Required(repository.Element("uuid"), "repository/uuid"),
            Format: null
        );
    }

    private static string Required(XElement? element, string what) =>
        (string?)element ?? throw new SvnCommandException($"svn info wrote no <{what}>.");
}
