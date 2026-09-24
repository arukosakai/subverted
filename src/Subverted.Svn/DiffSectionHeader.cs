namespace Subverted.Svn;

/// <summary>What the <c>Index:</c>, <c>---</c> and <c>+++</c> lines of one file's section say.</summary>
/// <param name="Name">The path as <c>svn diff</c> prints it: slash-separated, relative to its target.</param>
/// <param name="OldLabel">In the parentheses after the old name — <c>revision 4</c>.</param>
/// <param name="NewLabel">In the parentheses after the new name — <c>working copy</c>, or <c>revision 5</c>.</param>
internal sealed record DiffSectionHeader(string Name, string OldLabel, string NewLabel);
