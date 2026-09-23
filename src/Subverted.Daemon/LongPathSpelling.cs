using System.Runtime.InteropServices;
using System.Text;

namespace Subverted.Daemon;

/// <summary>
/// The long form of a path — what Explorer, a file dialog and wc.db all spell it as — rather than
/// the 8.3 short form <c>%TEMP%</c> hands out. Other platforms have one spelling already.
/// </summary>
public static class LongPathSpelling
{
    public static string Of(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        return OperatingSystem.IsWindows()
            ? PathRespelling.Respell(full, Path.Exists, AskWindows)
            : full;
    }

    /// <summary>
    /// A path Windows cannot respell comes back as given: the worst case is the two spellings
    /// staying apart, which is where this started, never a request failing because of it.
    /// </summary>
    private static string AskWindows(string existingPath)
    {
        var buffer = new StringBuilder(1024);
        var length = GetLongPathNameW(existingPath, buffer, buffer.Capacity);
        return length is > 0 and < 1024 ? buffer.ToString() : existingPath;
    }

    // DllImport rather than LibraryImport, for the reason StandardHandleInheritance gives.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLongPathNameW(string path, StringBuilder buffer, int size);
}
