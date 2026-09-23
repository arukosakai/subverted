using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;

namespace Subverted.App.Tests;

/// <summary>A diff pane over fakes, for tests whose rule is not about the pane.</summary>
internal static class DiffPanes
{
    public static DiffPaneViewModel Pane(
        IWorkingCopyDiff? diffs = null,
        TimeProvider? clock = null,
        IFileSizeReader? sizes = null,
        IFileLauncher? launcher = null
    ) =>
        new(
            diffs ?? new FakeWorkingCopyDiff(),
            sizes ?? new FakeFileSizeReader(),
            launcher ?? new FakeFileLauncher(),
            clock ?? new FakeTimeProvider()
        );
}
