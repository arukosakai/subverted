using System.Runtime.InteropServices;
using System.Text;

namespace Subverted.Daemon.Tests;

/// <summary>
/// Both ways Windows spells one existing path: the 8.3 short form (<c>ARUKO~1.SAK</c>) that
/// <c>%TEMP%</c> hands out, and the long form a file dialog or Explorer hands out.
/// </summary>
internal static class PathSpellings
{
    public static string Short(string path) => Ask(GetShortPathNameW, path);

    public static string Long(string path) => Ask(GetLongPathNameW, path);

    private static string Ask(Func<string, StringBuilder, int, int> call, string path)
    {
        var buffer = new StringBuilder(1024);
        var length = call(path, buffer, buffer.Capacity);
        return length == 0
            ? throw new IOException($"Windows could not respell '{path}'.")
            : buffer.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetShortPathNameW(string path, StringBuilder buffer, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLongPathNameW(string path, StringBuilder buffer, int size);
}
