namespace Subverted.Protocol;

/// <param name="Notifications">
/// SVN's own text, one <c>D</c> line per versioned node. Empty when everything named was
/// unversioned: SVN removes those without a word, which is why the front-end cannot simply print
/// this and call it a report.
/// </param>
public sealed record DeleteResponse(string Notifications) : DaemonResponse;
