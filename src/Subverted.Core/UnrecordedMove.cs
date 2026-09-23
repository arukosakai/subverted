namespace Subverted.Core;

/// <summary>
/// A file renamed or moved on disk without SVN being told: the versioned node is gone from its
/// recorded path, and its exact recorded content is sitting at a path SVN has never heard of.
/// </summary>
/// <remarks>
/// SVN has no concept of this. To it the pair is an unrelated <c>!</c> and <c>?</c>, and committing
/// in that state records a delete and an unrelated add — the file's history stops at the old name.
/// Pairing them by content is what lets someone find out while it is still repairable.
/// </remarks>
/// <param name="FromRelPath">The versioned path, now absent from disk.</param>
/// <param name="ToRelPath">The unversioned path holding its content.</param>
public sealed record UnrecordedMove(string FromRelPath, string ToRelPath);
