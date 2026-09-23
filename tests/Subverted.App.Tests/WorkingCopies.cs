using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

/// <summary>A working copy view over fakes, for tests that name only the fakes their rule reads.</summary>
internal static class WorkingCopies
{
    public static WorkingCopyViewModel View(
        IWorkingCopyStatus status,
        DiffPaneViewModel? pane = null,
        string path = "/studio/game",
        IFileLauncher? launcher = null,
        IFileRevealer? revealer = null,
        ITextClipboard? clipboard = null
    ) =>
        new(
            path,
            status,
            pane ?? DiffPanes.Pane(),
            launcher ?? new FakeFileLauncher(),
            revealer ?? new FakeFileRevealer(),
            clipboard ?? new FakeTextClipboard()
        );

    /// <summary>Picks the shown line for a path, as clicking it would.</summary>
    public static void Select(this WorkingCopyViewModel view, string key) =>
        view.SelectedEntry = view.Entries.Single(entry => entry.Key == key);
}
