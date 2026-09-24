using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>One confirmed delete and what came of it, as data for whatever keeps a record of writes.</summary>
/// <param name="Target">The deleted path, relative to the root.</param>
/// <param name="Notice">What the view says about it.</param>
/// <param name="Answer">The daemon's answer as it came, or <c>null</c> when no daemon answered.</param>
public sealed record DeleteAttempt(string Target, Notice Notice, DaemonResponse? Answer);
