namespace Subverted.Core;

/// <summary>
/// What the filesystem says about a file right now: enough to notice that it was written again,
/// never enough to say what changed. Not a content hash — two different edits of the same length
/// in the same tick compare equal, which is the filesystem's resolution and not something to fix here.
/// </summary>
/// <param name="LastWriteTimeUtc">At the platform's own resolution; 100 ns ticks on NTFS.</param>
public sealed record FileFingerprint(long Length, DateTime LastWriteTimeUtc);
