using System.Globalization;

namespace Subverted.App.Presentation;

/// <summary>What the commit button says: how many rows it would send, a rename counting as one.</summary>
public static class CommitButtonText
{
    public static string For(int rows) =>
        rows switch
        {
            <= 0 => "Commit",
            1 => "Commit 1 file",
            _ => string.Create(CultureInfo.InvariantCulture, $"Commit {rows:N0} files"),
        };
}
