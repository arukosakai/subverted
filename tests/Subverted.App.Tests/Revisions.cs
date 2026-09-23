using Microsoft.Extensions.Time.Testing;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;

namespace Subverted.App.Tests;

/// <summary>Revisions, rows and History view models built over fakes, for tests of History rules.</summary>
internal static class Revisions
{
    public static readonly DateTimeOffset Committed = new(2026, 9, 23, 13, 16, 0, TimeSpan.Zero);

    /// <summary>A revision that modified one file named after it, so every entry is distinct.</summary>
    public static RevisionEntry Entry(long revision) =>
        new(
            revision,
            "keiichi",
            Committed,
            $"change {revision}",
            [new ChangedPath($"/trunk/file{revision}.txt", PathChange.Modified, null, null)]
        );

    public static RevisionRow Row(
        long revision,
        string message = "a change",
        string author = "keiichi",
        params string[] paths
    ) =>
        RevisionRow.From(
            new RevisionEntry(
                revision,
                author,
                Committed,
                message,
                [.. paths.Select(path => new ChangedPath(path, PathChange.Modified, null, null))]
            ),
            TimeZoneInfo.Utc
        );

    public static HistoryViewModel View(
        FakeRevisionHistory? history = null,
        FakeRevisionDiff? diffs = null,
        TimeProvider? clock = null
    ) =>
        new(
            history ?? new FakeRevisionHistory(),
            new RevisionDiffPaneViewModel(
                diffs ?? new FakeRevisionDiff(),
                clock ?? new FakeTimeProvider()
            ),
            clock ?? new FakeTimeProvider()
        );
}
