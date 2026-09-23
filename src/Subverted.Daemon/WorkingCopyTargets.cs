namespace Subverted.Daemon;

/// <summary>
/// Which paths a request that writes is allowed to name. One request, one working copy: <c>svn</c>
/// runs in a single root, so a set of paths spanning two of them would commit half of each and call
/// it done.
/// </summary>
public static class WorkingCopyTargets
{
    /// <param name="rootPath">The root the request resolved to.</param>
    /// <returns>
    /// The first path not inside <paramref name="rootPath"/>, or <see langword="null"/> when every
    /// one of them is. Empty input is <see langword="null"/> — having no targets is a different
    /// complaint, made where it can be worded for the command the user actually typed.
    /// </returns>
    public static string? FirstOutside(
        string rootPath,
        IReadOnlyList<string> paths,
        StringComparison comparison
    ) => paths.FirstOrDefault(path => ContainingRoot.Of(path, [rootPath], comparison) is null);
}
