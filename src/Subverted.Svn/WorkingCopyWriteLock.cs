namespace Subverted.Svn;

/// <summary>
/// One row of wc.db's <c>WC_LOCK</c> — the write lock a client takes over part of a working copy
/// while it changes it, and leaves behind when it crashes. This is not the lock <c>sv lock</c>
/// takes: that one is a token held against the repository and prints as <c>K</c>.
/// </summary>
/// <param name="RelPath">
/// The locked directory, slash-separated, with the working-copy root as the empty string. Only
/// directories are ever locked.
/// </param>
/// <param name="LockedLevels">
/// How far below <paramref name="RelPath"/> the lock reaches: <c>0</c> that directory alone,
/// <c>-1</c> every level below it. Measured on 1.8.15 — see <see cref="WriteLockCoverage"/>.
/// </param>
public sealed record WorkingCopyWriteLock(string RelPath, int LockedLevels);
