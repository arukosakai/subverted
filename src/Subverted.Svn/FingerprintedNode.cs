namespace Subverted.Svn;

/// <param name="RelPath">Slash-separated path relative to the working-copy root.</param>
/// <param name="Sha1">Lowercase hex, of the node's content — recorded for a versioned node, computed for a file on disk.</param>
internal sealed record FingerprintedNode(string RelPath, string Sha1);
