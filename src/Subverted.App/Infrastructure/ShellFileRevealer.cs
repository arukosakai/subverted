using System.Runtime.InteropServices;
using Subverted.App.ViewModels;

namespace Subverted.App.Infrastructure;

/// <summary>
/// Reveals through the Windows shell's own call for it. <c>explorer /select,</c> was tried first and
/// on Windows 11 opens the folder without selecting anything, whether the path is quoted, glued to
/// the comma or passed apart; this selects it.
/// </summary>
/// <remarks>Call it from the UI thread, which is where the shell's COM apartment already is.</remarks>
public sealed class ShellFileRevealer : IFileRevealer
{
    public Task RevealAsync(string path)
    {
        if (NearestPresentPath.Of(path) is not { } shown)
        {
            return Task.CompletedTask;
        }

        if (SHParseDisplayName(shown, 0, out var item, 0, out _) != 0)
        {
            return Task.CompletedTask;
        }

        try
        {
            // No children: the item itself is what gets selected, in its parent's window.
            _ = SHOpenFolderAndSelectItems(item, 0, 0, 0);
        }
        finally
        {
            Marshal.FreeCoTaskMem(item);
        }

        return Task.CompletedTask;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name,
        nint bindingContext,
        out nint itemList,
        uint attributesWanted,
        out uint attributes
    );

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(
        nint folder,
        uint childCount,
        nint children,
        uint flags
    );
}
