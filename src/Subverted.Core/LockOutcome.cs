namespace Subverted.Core;

/// <param name="Notifications">
/// SVN's own text, one line per path it locked or unlocked, and empty when it did neither.
/// </param>
/// <param name="Refusals">
/// The paths SVN would not do, in its own words — one entry per warning it printed. Non-empty is
/// not the client failing: <c>svn lock</c> and <c>svn unlock</c> report a refused path as a warning
/// and exit zero, so this is the only place that difference is stated. A caller holds a lock it
/// asked for only if nothing about that path is in here.
/// </param>
public sealed record LockOutcome(string Notifications, IReadOnlyList<string> Refusals);
