namespace Subverted.Svn;

internal sealed record FileNode(long Length, DateTime LastWriteTimeUtc) : NodeSnapshot;
