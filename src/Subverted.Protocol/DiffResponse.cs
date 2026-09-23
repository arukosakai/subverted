namespace Subverted.Protocol;

/// <param name="UnifiedDiff">
/// SVN's own text, unparsed and empty when nothing changed. The front-end colours it by line
/// prefix and otherwise leaves it alone.
/// </param>
public sealed record DiffResponse(string UnifiedDiff) : DaemonResponse;
