using Subverted.Core;

namespace Subverted.Svn;

internal sealed record FileNode(long Length, DateTime LastWriteTimeUtc) : NodeSnapshot
{
    public FileFingerprint Fingerprint => new(Length, LastWriteTimeUtc);
}
