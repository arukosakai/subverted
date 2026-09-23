namespace Subverted.App.Presentation;

/// <param name="Label">What the row says, in the person's words rather than SVN's letter.</param>
/// <param name="Tone">Which colour family draws it.</param>
public sealed record ChangeBadge(string Label, ChangeTone Tone);
