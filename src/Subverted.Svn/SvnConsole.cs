using System.Runtime.InteropServices;

namespace Subverted.Svn;

/// <summary>
/// Gives this process a UTF-8 console for every <c>svn</c> it starts to inherit. On Windows svn
/// writes paths — diff headers, status text, errors — in its console's output code page, and reads
/// its arguments through it too; <c>LC_ALL</c> changes neither. D33 has the measurements.
/// </summary>
public static class SvnConsole
{
    private const uint AllocateWithoutWindow = 2;

    /// <summary>
    /// Call once, before the first <see cref="SvnCommand"/> runs. The code pages belong to the
    /// console, not to this process, so a console shared with a terminal is switched for as long
    /// as the returned scope lives and put back when it is disposed. Failures leave things as they were.
    /// </summary>
    public static IDisposable UseUtf8()
    {
        ConsoleCodePages? current = OperatingSystem.IsWindows() ? CurrentCodePages() : null;

        switch (Utf8ConsoleRule.StepFor(OperatingSystem.IsWindows(), current))
        {
            case Utf8ConsoleStep.SwitchCodePages:
                SwitchToUtf8();
                return new CodePageRestore(current!.Value);
            case Utf8ConsoleStep.AllocateWindowlessConsole:
                AllocateWindowlessConsole();
                return new CodePageRestore(null);
            default:
                return new CodePageRestore(null);
        }
    }

    /// <remarks>
    /// GetConsoleOutputCP answers 0 for a process with no console, which is how absence is told
    /// apart from a real code page.
    /// </remarks>
    private static ConsoleCodePages? CurrentCodePages()
    {
        var output = GetConsoleOutputCP();
        return output == 0 ? null : new ConsoleCodePages(output, GetConsoleCP());
    }

    private static void SwitchToUtf8()
    {
        SetConsoleOutputCP(ConsoleCodePages.Utf8);
        SetConsoleCP(ConsoleCodePages.Utf8);
    }

    /// <remarks>
    /// Plain AllocConsole would put a window on the desktop. The windowless mode only exists from
    /// Windows 11 24H2; before that the process stays without a console, as it was.
    /// </remarks>
    private static void AllocateWindowlessConsole()
    {
        var options = new AllocConsoleOptions { Mode = AllocateWithoutWindow };
        try
        {
            if (AllocConsoleWithOptions(ref options, out _) >= 0)
            {
                SwitchToUtf8();
            }
        }
        catch (EntryPointNotFoundException) { }
    }

    private sealed class CodePageRestore(ConsoleCodePages? previous) : IDisposable
    {
        public void Dispose()
        {
            if (previous is { } codePages)
            {
                SetConsoleOutputCP(codePages.Output);
                SetConsoleCP(codePages.Input);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AllocConsoleOptions
    {
        public uint Mode;
        public int UseShowWindow;
        public ushort ShowWindow;
    }

    // DllImport rather than LibraryImport, for the reason StandardHandleInheritance gives.
    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleOutputCP();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleCP();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleOutputCP(uint codePage);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCP(uint codePage);

    [DllImport("kernel32.dll")]
    private static extern int AllocConsoleWithOptions(
        ref AllocConsoleOptions options,
        out int result
    );
}
