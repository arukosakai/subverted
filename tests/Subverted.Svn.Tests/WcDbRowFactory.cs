using Subverted.Core;
using Subverted.Svn;

namespace Subverted.Svn.Tests;

/// <summary>
/// Builds wc.db rows for tests. Every field has a boring default so each test can state only the
/// one or two columns its rule actually depends on.
/// </summary>
/// <remarks>
/// The defaults describe a committed BASE node at depth 2. A local layer over it has to state an
/// op_depth the path could really carry: at <c>assets/hero.png</c>, 2 is an operation on the file
/// itself and 1 is the file sitting inside a copied <c>assets</c>, and the two resolve differently.
/// </remarks>
internal static class WcDbRowFactory
{
    public static WcDbRow Row(
        string presence = "normal",
        int opDepth = 0,
        string relPath = "assets/hero.png",
        NodeKind kind = NodeKind.File,
        int? lowerOpDepth = null,
        bool hasCopySource = true,
        bool hasConflict = false,
        long? recordedSize = 100,
        long? recordedModTime = 1_704_067_200_000_000,
        string? checksum = null,
        bool isTranslated = false,
        PropertyStatus propertyStatus = PropertyStatus.Unmodified
    ) =>
        new(
            RelPath: relPath,
            OpDepth: opDepth,
            Presence: presence,
            Kind: kind,
            BaseRevision: 42,
            RawTranslatedSize: recordedSize,
            RecordedModTime: recordedModTime,
            LowerOpDepth: lowerOpDepth,
            HasCopySource: hasCopySource,
            HasConflict: hasConflict,
            Changelist: null,
            HasLockToken: false,
            Checksum: checksum,
            IsTranslated: isTranslated,
            PropertyStatus: propertyStatus
        );
}
