namespace Subverted.App.ViewModels;

/// <summary>One write in the output log, as SmartSVN keeps one: when, what, and what came of it.</summary>
/// <param name="Operation">The write, as the person asked for it: <c>Commit</c> or <c>Revert</c>.</param>
/// <param name="Subject">What it was about: the paths it named, and a commit's first message line.</param>
/// <param name="Notice">What the view said; its detail is SVN's own text.</param>
/// <param name="At">Local time, as the clock gave it: the log is read by the person at this machine.</param>
public sealed record OutputLine(DateTimeOffset At, string Operation, string Subject, Notice Notice);
