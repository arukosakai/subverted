namespace Subverted.Svn.Tests;

/// <summary>
/// Tests run svn in-process, so they need what the daemon does at startup. Scoped to the run and
/// put back afterwards, because the console may be the developer's own terminal.
/// </summary>
public static class SvnConsoleForTheRun
{
    private static IDisposable? _utf8Console;

    [Before(HookType.Assembly)]
    public static void SwitchToUtf8() => _utf8Console = SvnConsole.UseUtf8();

    [After(HookType.Assembly)]
    public static void PutBack() => _utf8Console?.Dispose();
}
