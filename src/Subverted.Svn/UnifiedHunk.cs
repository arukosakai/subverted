namespace Subverted.Svn;

/// <summary>
/// One <c>@@</c> block: <paramref name="OldCount"/> lines from zero-based line
/// <paramref name="OldStart"/> of the old text, against <paramref name="NewCount"/> from
/// <paramref name="NewStart"/> of the new, context included.
/// </summary>
internal readonly record struct UnifiedHunk(int OldStart, int OldCount, int NewStart, int NewCount);
