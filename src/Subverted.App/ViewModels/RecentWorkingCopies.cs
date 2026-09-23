namespace Subverted.App.ViewModels;

/// <summary>The sidebar's list: most recent first, each copy once, and short enough to scan.</summary>
public static class RecentWorkingCopies
{
    public const int Capacity = 8;

    /// <param name="comparison">
    /// How this platform compares paths. Taken as an argument so both answers are tested on either
    /// operating system — on Windows <c>C:\Art</c> and <c>c:\art</c> are one working copy.
    /// </param>
    public static IReadOnlyList<string> Opened(
        IReadOnlyList<string> recent,
        string path,
        StringComparison comparison
    ) =>
        [
            path,
            .. recent
                .Where(existing => !string.Equals(existing, path, comparison))
                .Take(Capacity - 1),
        ];
}
