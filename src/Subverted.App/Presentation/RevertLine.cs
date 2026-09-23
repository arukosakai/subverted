namespace Subverted.App.Presentation;

/// <summary>One path a revert touches, and what happens to it.</summary>
/// <param name="RelPath">Slash-separated, relative to the root.</param>
/// <param name="What">What revert does to it, in the person's words.</param>
/// <param name="LosesWork">
/// Whether something only this working copy had is thrown away — an edit, a property change, a
/// replacement. False for what revert only puts back or un-schedules.
/// </param>
public sealed record RevertLine(string RelPath, string What, bool LosesWork);
