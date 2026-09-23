using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// One row of the highest-<c>op_depth</c> projection of the NODES table — i.e. the node as the
/// working copy currently sees it, with local adds/deletes/copies already layered over BASE.
/// </summary>
/// <param name="OpDepth">0 for a pristine BASE node; higher for locally modified tree structure.</param>
/// <param name="BaseRevision">
/// The revision <c>svn status -v</c> prints: BASE's, not the projected row's, and null when the
/// node has no BASE row at all. The distinction is not cosmetic — at op_depth &gt; 0 the NODES
/// revision column means the <em>copyfrom</em> revision, so a copy would report its source's
/// revision where svn prints <c>-</c>, and a delete would report nothing where svn prints the
/// revision being deleted.
/// </param>
/// <param name="Presence">Raw SVN presence: normal, not-present, incomplete, base-deleted, excluded, server-excluded.</param>
/// <param name="LowerOpDepth">
/// The op_depth of the next layer down at this path, or null when this row is the only one. Not
/// "is there a BASE row": a replace inside a copied directory sits over the copy and has no BASE.
/// </param>
/// <param name="HasCopySource">
/// The row names a repository node. Every BASE row does; above BASE only a copy or a move does, and
/// a plain add or a delete names nothing.
/// </param>
/// <param name="RawTranslatedSize">As stored; read it through <see cref="RecordedSize"/>.</param>
/// <param name="RecordedModTime">APR time (microseconds since Unix epoch), or null if never recorded.</param>
/// <param name="IsTranslated">Working file is a translation of its pristine, so hashing it proves nothing.</param>
/// <param name="PropertyStatus">Already resolved at read time, because the raw skels are of no use past it.</param>
internal sealed record WcDbRow(
    string RelPath,
    int OpDepth,
    string Presence,
    NodeKind Kind,
    long? BaseRevision,
    long? RawTranslatedSize,
    long? RecordedModTime,
    int? LowerOpDepth,
    bool HasCopySource,
    bool HasConflict,
    string? Changelist,
    bool HasLockToken,
    string? Checksum,
    bool IsTranslated,
    PropertyStatus PropertyStatus
)
{
    /// <summary>
    /// Post-translation size SVN last recorded, or null when it has none.
    /// </summary>
    /// <remarks>
    /// SVN invalidates this cache by writing -1 rather than NULL — setting <c>svn:eol-style</c>
    /// does it, because the recorded size stops meaning anything once translation changes. Taking
    /// -1 for a real size reports an untouched file as modified.
    ///
    /// SVN blanks <c>last_mod_time</c> to 0 in the same write, but that needs no sentinel of its
    /// own: an unrecorded size already sends the node to the content compare, and 0 is a
    /// legitimate APR time.
    /// </remarks>
    public long? RecordedSize => RawTranslatedSize is null or < 0 ? null : RawTranslatedSize;

    /// <summary>
    /// True for nodes wc.db tracks but that were never materialised on disk — sparse-checkout
    /// exclusions, authz-hidden paths, and the not-present tombstones left behind by deletes.
    /// </summary>
    public bool IsAbsentFromWorkingCopy =>
        Presence is "excluded" or "server-excluded" || (Presence == "not-present" && OpDepth == 0);

    /// <summary>
    /// This row is a local operation of its own — an add, a copy, a move or a replace — rather than
    /// a node that one carried along. op_depth is the depth of the path the operation was aimed at.
    /// </summary>
    public bool IsOperationRoot => OpDepth > 0 && OpDepth == DepthOf(RelPath);

    /// <summary>
    /// A node a copied directory brought with it. Every plain add is an operation root of its own,
    /// so a normal row below its operation's root can only be part of a copy.
    /// </summary>
    public bool IsWithinCopy => IsWithinCopyAt(RelPath, OpDepth, Presence);

    /// <summary>Taken apart from a row so the reader can ask it before the row exists.</summary>
    internal static bool IsWithinCopyAt(string relPath, int opDepth, string presence) =>
        opDepth > 0 && presence == "normal" && opDepth < DepthOf(relPath);

    private static int DepthOf(string relPath) =>
        relPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
}
