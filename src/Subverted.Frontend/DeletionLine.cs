namespace Subverted.Frontend;

/// <summary>One path a delete reaches, and what happens to it.</summary>
/// <param name="RelPath">Slash-separated, relative to the root.</param>
/// <param name="What">What the delete does to it, in the person's words.</param>
/// <param name="LosesWork">
/// Whether something only this working copy had goes with it — an edit, an add, a file SVN does not
/// track. False for what SVN still has and Revert or the copy's source can bring back.
/// </param>
public sealed record DeletionLine(string RelPath, string What, bool LosesWork);
