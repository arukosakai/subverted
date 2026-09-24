namespace Subverted.App.Tests;

/// <summary>A socket path nothing listens on, for the tests that need a daemon to be absent.</summary>
internal static class UnusedSocket
{
    /// <remarks>
    /// Straight under the temp directory and short: AF_UNIX caps the whole path near 104 bytes on
    /// macOS, whose temp directory alone is about half of that.
    /// </remarks>
    public static string NewPath() =>
        Path.Combine(Path.GetTempPath(), $"sv-{Guid.NewGuid():N}"[..12] + ".sock");
}
