namespace Subverted.Protocol;

/// <param name="Notifications">
/// SVN's own text, one line per scheduled path and empty when it scheduled nothing. The front-end
/// prints it rather than rewording it, for the reason given on <see cref="DiffResponse"/>.
/// </param>
public sealed record AddResponse(string Notifications) : DaemonResponse;
