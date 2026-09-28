namespace Subverted.Core;

/// <summary>
/// One path the repository has moved past this working copy's BASE on — a <c>*</c> in
/// <c>svn status -u</c>. Something an update would bring down, not something changed here.
/// </summary>
/// <param name="RelPath">Relative to the working-copy root, <c>/</c>-separated; the root is the empty string.</param>
/// <param name="Change">What the server did to the node itself, or null when only its properties changed.</param>
/// <param name="PropertiesChanged">Its properties changed on the server.</param>
public sealed record IncomingChange(string RelPath, PathChange? Change, bool PropertiesChanged);
