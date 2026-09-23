using System.Text.Json.Serialization;

namespace Subverted.Core;

/// <summary>
/// The newest revision a history read may list. Designed for inheritance, but only from inside
/// this assembly: the private protected constructor closes the set so every reader of it can be
/// exhaustive. Asking with none at all means SVN's own default, the target's BASE.
/// </summary>
/// <remarks>
/// Serialisation metadata lives here rather than in Protocol because a closed hierarchy can only be
/// described to the source generator on its base type. It is attributes from the base library, not
/// a package.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(HistoryFromHead), "head")]
[JsonDerivedType(typeof(HistoryFromRevision), "revision")]
public abstract record HistoryStart
{
    private protected HistoryStart() { }
}
