namespace Subverted.Protocol;

/// <summary>
/// Where the daemon listens and front-ends connect. One well-known per-user path, so neither side
/// needs configuration and two users on one machine do not collide.
/// </summary>
public static class DaemonSocketPath
{
    /// <summary>Set to run a second daemon, or to point a front-end at a throwaway one.</summary>
    public const string OverrideVariable = "SUBVERTED_SOCKET";

    private const string DirectoryName = "subverted";
    private const string FileName = "daemon.sock";

    /// <param name="socketOverride">Used verbatim when it has content; whitespace counts as unset.</param>
    /// <param name="runtimeDirectory">Per-user base directory. Must be absolute.</param>
    /// <exception cref="ArgumentException">
    /// No override, and <paramref name="runtimeDirectory"/> is empty or relative — a relative
    /// socket path binds somewhere different for every caller's working directory.
    /// </exception>
    public static string Resolve(string? socketOverride, string runtimeDirectory)
    {
        if (!string.IsNullOrWhiteSpace(socketOverride))
        {
            return socketOverride;
        }

        if (!Path.IsPathRooted(runtimeDirectory))
        {
            throw new ArgumentException(
                $"A socket path needs an absolute runtime directory; got '{runtimeDirectory}'.",
                nameof(runtimeDirectory)
            );
        }

        return Path.Combine(runtimeDirectory, DirectoryName, FileName);
    }

    public static string FromEnvironment() =>
        Resolve(Environment.GetEnvironmentVariable(OverrideVariable), RuntimeDirectory());

    private static string RuntimeDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        // AF_UNIX caps the *whole* path near 104 bytes on macOS, so the fallback goes under the
        // system temp directory rather than under $HOME, which can be arbitrarily deep.
        return Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime
            ? runtime
            : Path.Combine(Path.GetTempPath(), Environment.UserName);
    }
}
