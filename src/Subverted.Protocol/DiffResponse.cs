using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="UnifiedDiff">
/// Unified diff text in <c>svn diff</c>'s shape, empty when nothing changed. SVN's own text, unparsed,
/// unless <paramref name="Context"/> is wider than <see cref="DiffContext.Default"/>: then the daemon
/// wrote it in the same shape, because <c>svn diff</c> cannot print more than three lines.
/// </param>
/// <param name="Context">
/// The context the text was built with. <see cref="DiffContext.Default"/> whenever SVN wrote it —
/// including when more was asked for and this file could not have it — and <see langword="null"/>
/// only from a daemon that predates the field, which always sent SVN's.
/// </param>
public sealed record DiffResponse(string UnifiedDiff, DiffContext? Context = null) : DaemonResponse;
