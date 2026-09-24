namespace Subverted.Svn;

/// <summary>
/// <paramref name="Length"/> lines that are the same on both sides, starting at zero-based line
/// <paramref name="OldStart"/> of the old text and <paramref name="NewStart"/> of the new.
/// </summary>
internal readonly record struct MatchedRun(int OldStart, int NewStart, int Length);
