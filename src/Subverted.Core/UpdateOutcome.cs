namespace Subverted.Core;

/// <param name="Revision">
/// The revision the working copy now stands at, or <see langword="null"/> when SVN did not say. It
/// is reported whether or not anything came down — SVN prints "At revision N" for an update that
/// brought nothing and "Updated to revision N" for one that did, and both mean the same thing here.
/// </param>
/// <param name="Conflicts">
/// How many conflicts SVN counted, text, property and tree together. Conflicts, not paths: one file
/// whose content and properties both conflict is counted twice, which is how SVN counts it.
/// </param>
/// <param name="SkippedPaths">
/// How many paths SVN declined to touch. Not a conflict — nothing was merged and nothing was left
/// half-done — but equally something the update did not do, and it is reported so it is not silent.
/// </param>
/// <param name="Notifications">
/// SVN's own output, unparsed. It already names every path it touched and how; re-wording it would
/// only produce a second answer to disagree with.
/// </param>
public sealed record UpdateOutcome(
    long? Revision,
    int Conflicts,
    int SkippedPaths,
    string Notifications
);
