using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>One press of Commit and what came of it, as data for whatever keeps a record of writes.</summary>
/// <param name="SentRelPaths">The paths the request named, both halves of every rename.</param>
/// <param name="Message">The log message sent.</param>
/// <param name="Notice">What the view says about it; its kind tells the three answers apart.</param>
/// <param name="Answer">
/// The daemon's answer as it came — its <c>Notifications</c> are SVN's own text — or <c>null</c> when
/// no daemon answered.
/// </param>
public sealed record CommitAttempt(
    IReadOnlyList<string> SentRelPaths,
    string Message,
    Notice Notice,
    DaemonResponse? Answer
);
