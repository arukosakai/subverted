namespace Subverted.App.Infrastructure;

/// <summary>A deleted or missing change is not on disk, so the folder it was in is shown instead.</summary>
public static class NearestPresentPath
{
    /// <returns>The path or its nearest ancestor that exists; <c>null</c> when not even a root does.</returns>
    public static string? Of(string path)
    {
        for (
            string? candidate = path;
            candidate is not null;
            candidate = Path.GetDirectoryName(candidate)
        )
        {
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
