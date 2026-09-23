using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Reads the document <c>svn status --xml</c> writes — D1's fallback for a working copy whose wc.db
/// this build cannot read. Pure: hand it the text and it hands back entries, so every shape SVN can
/// emit is a test rather than a working copy.
/// </summary>
/// <remarks>
/// <c>svn status</c> reports no node kind, so every entry comes back <see cref="NodeKind.Unknown"/>.
/// Nothing renders kind today; a consumer that starts to has to ask the filesystem for it.
/// </remarks>
public static class SvnStatusXml
{
    /// <param name="directorySeparator">
    /// The separator SVN wrote its paths with. Status echoes the target it was given and does not
    /// normalise, so this is the platform's own — taken as an argument so both platforms' output is
    /// covered wherever the tests run.
    /// </param>
    /// <returns>Entries in the order SVN listed them: alphabetical, changelisted nodes last.</returns>
    /// <exception cref="SvnCommandException">
    /// The text is not a status document, or carries a state this build does not model. An
    /// unmodelled state fails the whole call rather than being guessed at: a status list that
    /// quietly mislabels a node is exactly what hides work from a commit.
    /// </exception>
    public static IReadOnlyList<WorkingCopyEntry> Parse(string xml, char directorySeparator)
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

        return
        [
            .. WithoutExternalContents([
                .. status.Descendants("entry").Select(entry => Entry(entry, directorySeparator)),
            ]),
        ];
    }

    /// <summary>
    /// <c>svn status --xml</c> lists an external <em>twice</em>: once as the placeholder in this
    /// working copy, and again — followed by everything inside it — because it recurses into the
    /// external and reports that checkout too.
    /// </summary>
    /// <remarks>
    /// Those nodes belong to a different working copy, so their paths mean nothing here and the
    /// duplicate would give one path two statuses. Only the placeholder survives, which is what the
    /// wc.db reader produces from the <c>EXTERNALS</c> table.
    /// </remarks>
    private static IEnumerable<WorkingCopyEntry> WithoutExternalContents(
        IReadOnlyList<WorkingCopyEntry> entries
    )
    {
        var externals = entries
            .Where(entry => entry.Status == NodeStatus.External)
            .Select(entry => entry.RelPath)
            .ToHashSet(StringComparer.Ordinal);

        if (externals.Count == 0)
        {
            return entries;
        }

        return entries.Where(entry =>
            entry.Status == NodeStatus.External || !IsAtOrBeneath(entry.RelPath, externals)
        );
    }

    private static bool IsAtOrBeneath(string relPath, HashSet<string> externals) =>
        externals.Contains(relPath)
        || externals.Any(external => relPath.StartsWith($"{external}/", StringComparison.Ordinal));

    private static WorkingCopyEntry Entry(XElement entry, char directorySeparator)
    {
        var path = Attribute(entry, "path");
        var wcStatus =
            entry.Element("wc-status")
            ?? throw new SvnCommandException(
                $"svn status wrote an <entry> for '{path}' with no <wc-status>."
            );

        var itemStatus = Status(Attribute(wcStatus, "item"));
        var properties = Attribute(wcStatus, "props");

        // Resolved before it is known whether it will be kept, so a property state this build
        // cannot read fails the node rather than being waved through on the ones that discard it.
        var propertyStatus = Properties(properties);

        // SVN prints a property conflict in its second column and leaves the first blank. Subverted
        // has no conflicted state on the property axis, so the whole node is conflicted — the same
        // answer the wc.db path gives, and the one that over-reports rather than hiding a conflict.
        var isConflicted = itemStatus == NodeStatus.Conflicted || properties == "conflicted";

        return new WorkingCopyEntry(
            RelPath: RelativePath(path, directorySeparator),
            Kind: NodeKind.Unknown,
            Status: isConflicted ? NodeStatus.Conflicted : itemStatus,
            PropertyStatus: HasLocalTreeLayer(itemStatus)
                ? PropertyStatus.Unmodified
                : propertyStatus,
            Revision: Revision(wcStatus.Attribute("revision")),
            Changelist: Changelist(entry),
            IsConflicted: isConflicted,
            HasLockToken: wcStatus.Element("lock") is not null,
            // An <entry> for an unlocked node carries no wc-locked attribute at all, so absent is
            // the healthy case and the only true value SVN writes is "true".
            IsWriteLocked: (string?)wcStatus.Attribute("wc-locked") == "true",
            // Absent on a missing or obstructed copy too, which is the rule the wc.db reader
            // reaches from the other side — both readers drop the + there.
            IsCopied: (string?)wcStatus.Attribute("copied") == "true"
        );
    }

    private static NodeStatus Status(string item) =>
        item switch
        {
            "normal" => NodeStatus.Unmodified,
            "modified" => NodeStatus.Modified,
            "added" => NodeStatus.Added,
            "deleted" => NodeStatus.Deleted,
            "replaced" => NodeStatus.Replaced,
            "missing" => NodeStatus.Missing,
            "incomplete" => NodeStatus.Incomplete,
            "conflicted" => NodeStatus.Conflicted,
            "unversioned" => NodeStatus.Unversioned,
            "ignored" => NodeStatus.Ignored,
            "obstructed" => NodeStatus.Obstructed,
            "external" => NodeStatus.External,
            _ => throw new SvnCommandException(
                $"svn status reported the node state '{item}', which Subverted does not model. "
                    + "Failing is deliberate: a status list that mislabels a node hides work."
            ),
        };

    /// <summary>
    /// Added, replaced and deleted are SVN's local tree layers — wc.db's <c>op_depth &gt; 0</c>.
    /// <c>svn status</c> leaves the property column blank on them, and does so *contradicting its
    /// own XML*, which reports <c>props="modified"</c> on a copy whose properties differ from its
    /// source. <see cref="PropertyStatusResolver"/> reaches the same answer from op_depth, and the
    /// two readers have to agree.
    /// </summary>
    private static bool HasLocalTreeLayer(NodeStatus status) =>
        status is NodeStatus.Added or NodeStatus.Replaced or NodeStatus.Deleted;

    private static PropertyStatus Properties(string props) =>
        props switch
        {
            "none" or "normal" => PropertyStatus.Unmodified,
            "modified" or "conflicted" => PropertyStatus.Modified,
            _ => throw new SvnCommandException(
                $"svn status reported the property state '{props}', which Subverted does not model."
            ),
        };

    /// <summary>
    /// SVN writes <c>-1</c> for a node with no BASE revision — a local add — and leaves the
    /// attribute off entirely for one it does not version at all. Both mean "no revision".
    /// </summary>
    private static long? Revision(XAttribute? revision)
    {
        if (revision is null)
        {
            return null;
        }

        if (!long.TryParse(revision.Value, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new SvnCommandException(
                $"svn status wrote wc-status/@revision as '{revision.Value}', "
                    + "which is not a revision number."
            );
        }

        return parsed < 0 ? null : parsed;
    }

    /// <summary>
    /// SVN groups changelisted nodes under a <c>&lt;changelist&gt;</c> element rather than marking
    /// each one, so a node's changelist is the name of the element it sits in.
    /// </summary>
    private static string? Changelist(XElement entry) =>
        (string?)entry.Ancestors("changelist").FirstOrDefault()?.Attribute("name");

    /// <summary>
    /// Run in the root against <c>.</c>, <c>svn status</c> echoes every path relative to it — and
    /// the root itself as <c>.</c>, which the model writes as the empty string.
    /// </summary>
    private static string RelativePath(string path, char directorySeparator) =>
        path == "." ? string.Empty : path.Replace(directorySeparator, '/');

    private static string Attribute(XElement element, string name) =>
        (string?)element.Attribute(name)
        ?? throw new SvnCommandException(
            $"svn status wrote a <{element.Name.LocalName}> with no {name} attribute."
        );
}
