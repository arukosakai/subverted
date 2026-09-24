using Subverted.Protocol;

namespace Subverted.Frontend;

/// <summary>
/// Whether a lock or an unlock left something for a person, for every front-end, so <c>sv lock</c>'s
/// exit code and the app's notice cannot disagree about it. <c>svn lock</c> exits zero after
/// refusing every path, so the refusals are the only place the answer is.
/// </summary>
public static class LockAttention
{
    /// <returns>True when any path was not locked: the caller does not hold it.</returns>
    public static bool IsNeeded(LockResponse response) => response.Refusals.Count > 0;

    /// <returns>True when any lock had already gone — stolen or broken — before it was given back.</returns>
    public static bool IsNeeded(UnlockResponse response) => response.Refusals.Count > 0;
}
