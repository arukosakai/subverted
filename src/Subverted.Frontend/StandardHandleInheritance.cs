using System.Runtime.InteropServices;

namespace Subverted.Frontend;

/// <summary>
/// Stops child processes inheriting this one's standard handles.
/// </summary>
/// <remarks>
/// Windows gives a child *every* inheritable handle the parent holds, not only the three it was
/// handed — so the daemon kept a copy of <c>sv</c>'s own stdout, and a shell running
/// <c>sv st | grep x</c> never saw end-of-file on a command that had already exited. Redirecting
/// the daemon's three handles does not help; the copy is the problem. Clearing the flag changes
/// what children receive, not what this process can still write to.
/// </remarks>
internal static class StandardHandleInheritance
{
    private const int StandardInput = -10;
    private const int StandardOutput = -11;
    private const int StandardError = -12;
    private const uint HandleFlagInherit = 0x1;

    /// <summary>
    /// Call before starting a process that outlives this one. Failures are ignored: the worst case
    /// is the hang this prevents, and there is nothing useful to tell the user about it.
    /// </summary>
    public static void Disable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var handle in (int[])[StandardInput, StandardOutput, StandardError])
        {
            var native = GetStdHandle(handle);
            if (native != nint.Zero && native != -1)
            {
                SetHandleInformation(native, HandleFlagInherit, 0);
            }
        }
    }

    // DllImport rather than LibraryImport: the generated marshalling for the latter needs
    // AllowUnsafeBlocks turned on project-wide, which is a large switch to throw for two calls
    // whose arguments are already blittable.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(nint hObject, uint dwMask, uint dwFlags);
}
