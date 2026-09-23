namespace Subverted.Protocol;

/// <param name="Notifications">SVN's own text, one line per path it locked.</param>
/// <param name="Refusals">
/// The paths SVN would not lock, in its own words. Non-empty means the caller does <b>not</b> hold
/// them — <c>svn lock</c> reports a refusal as a warning and exits zero, so a front-end that drops
/// this tells an artist to start work on a file somebody else is already editing.
/// </param>
public sealed record LockResponse(string Notifications, IReadOnlyList<string> Refusals)
    : DaemonResponse;
