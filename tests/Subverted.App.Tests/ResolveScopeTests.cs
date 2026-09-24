using Subverted.App.Presentation;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

public sealed class ResolveScopeTests
{
    private static readonly ChangeRow[] Listed =
    [
        Row(Conflicted("art/hero.png")),
        Row(Entry("art/villain.png")),
        Row(Conflicted("art/sub/crate.png")),
        Row(Conflicted("artwork.png")),
        Row(Conflicted("levels/forest.map")),
    ];

    /// <summary>Resolve goes to infinite depth, and only a conflict is something it touches.</summary>
    [Test]
    public async Task A_folder_reaches_every_conflict_beneath_it_in_path_order_and_nothing_else()
    {
        var scope = ResolveScope.For(
            Row(Entry("art", kind: NodeKind.Directory)),
            ConflictResolution.Theirs,
            Listed.Reverse()
        );

        await Assert
            .That(string.Join(",", scope.Lines))
            .IsEqualTo("art/hero.png,art/sub/crate.png");
        await Assert.That(scope.Target).IsEqualTo("art");
        await Assert.That(scope.Resolution).IsEqualTo(ConflictResolution.Theirs);
    }

    [Test]
    public async Task A_file_that_is_not_conflicted_reaches_nothing()
    {
        var scope = ResolveScope.For(
            Row(Entry("art/villain.png")),
            ConflictResolution.Mine,
            Listed
        );

        await Assert.That(scope.Lines).IsEmpty();
    }

    [Test]
    public async Task One_path_is_asked_about_by_name()
    {
        var scope = ResolveScope.For(Listed[0], ConflictResolution.Theirs, Listed);

        await Assert.That(scope.Title).IsEqualTo("Keep the incoming version of art/hero.png?");
        await Assert
            .That(scope.Warning)
            .IsEqualTo("Your version is thrown away and cannot be brought back.");
    }

    [Test]
    public async Task Several_paths_are_asked_about_by_count_and_folder()
    {
        var scope = new ResolveScope("art", ConflictResolution.Theirs, ["art/a.png", "art/b.png"]);

        await Assert
            .That(scope.Title)
            .IsEqualTo("Keep the incoming version of 2 conflicted paths in art?");
        await Assert
            .That(scope.Warning)
            .IsEqualTo("Your version of each is thrown away and cannot be brought back.");
    }

    private static WorkingCopyEntry Conflicted(string relPath) =>
        Entry(relPath, NodeStatus.Conflicted, isConflicted: true);

    private static ChangeRow Row(WorkingCopyEntry entry) => ChangeRow.From(entry);
}
