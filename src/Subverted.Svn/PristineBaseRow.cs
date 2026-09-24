using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// What wc.db says about one node's BASE text: enough to decide whether its diff can be written
/// from the pristine and the working file, and where that pristine is.
/// </summary>
/// <param name="OpDepth">Of the node's highest layer; 0 is BASE with no local add, copy or delete over it.</param>
/// <param name="Presence">Of that layer, as <see cref="WcDbRow.Presence"/>.</param>
/// <param name="Revision">BASE's revision — what <c>svn diff</c> prints as <c>(revision N)</c> — when the layer is BASE.</param>
/// <param name="Checksum"><c>NODES.checksum</c>, which names the pristine.</param>
/// <param name="PristineProperties"><c>NODES.properties</c>.</param>
/// <param name="WorkingProperties"><c>ACTUAL_NODE.properties</c>; null when unchanged.</param>
/// <param name="HasConflict">Any conflict is recorded on the node.</param>
/// <param name="PristineSize">
/// <c>PRISTINE.size</c>, or null when the pristine table has no row for the checksum.
/// </param>
/// <param name="PristineIsPlain">
/// <c>PRISTINE.compression</c> is null. Every pristine 1.8.15 wrote was; a store that says
/// otherwise is not read.
/// </param>
internal sealed record PristineBaseRow(
    int OpDepth,
    string Presence,
    NodeKind Kind,
    long? Revision,
    string? Checksum,
    byte[]? PristineProperties,
    byte[]? WorkingProperties,
    bool HasConflict,
    long? PristineSize,
    bool PristineIsPlain
)
{
    /// <summary>
    /// How to normalise the working file before comparing it with the pristine, when that is how
    /// <c>svn diff</c> would answer for this node; <see langword="null"/> when its answer is anything
    /// else — an add, copy or delete, a property change, a conflict — or the text is not comparable.
    /// </summary>
    public LineEndingStyle? ComparableLineEndings
    {
        get
        {
            var isPlainBaseFile =
                OpDepth == 0
                && Presence == "normal"
                && Kind == NodeKind.File
                && Revision is not null
                && !HasConflict
                && PristineSize is not null
                && PristineIsPlain
                && SvnChecksum.TryParseSha1(Checksum) is not null;
            var propertiesUnchanged =
                PropertyStatusResolver.Resolve(
                    OpDepth,
                    false,
                    WorkingProperties,
                    PristineProperties
                ) == PropertyStatus.Unmodified;

            return
                isPlainBaseFile
                && propertiesUnchanged
                && SvnPropertySkel.Parse(PristineProperties) is { } properties
                ? ComparableText.LineEndingsOf(properties)
                : null;
        }
    }
}
