using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// The rule that turns wc.db's <c>WC_LOCK</c> rows into the directories <c>svn status</c> prints
/// <c>L</c> against. Every case here was read off real <c>svn</c> first — the depth rule is
/// documented nowhere, and <c>SvnCleanupIntegrationTests</c> is what pins these answers to the
/// client rather than to this file.
/// </summary>
public sealed class WriteLockCoverageTests
{
    private const int EveryLevel = -1;

    [Test]
    public async Task A_working_copy_with_no_locks_reaches_nothing()
    {
        var coverage = new WriteLockCoverage([]);

        await Assert.That(coverage.Any).IsFalse();
        await Assert.That(coverage.Reaches(NodeKind.Directory, string.Empty)).IsFalse();
        await Assert.That(coverage.Reaches(NodeKind.Directory, "sub")).IsFalse();
    }

    [Test]
    public async Task The_shared_empty_coverage_reaches_nothing_either()
    {
        await Assert.That(WriteLockCoverage.None.Any).IsFalse();
        await Assert.That(WriteLockCoverage.None.Reaches(NodeKind.Directory, "sub")).IsFalse();
    }

    [Test]
    public async Task A_lock_reaches_the_directory_it_names()
    {
        var coverage = new WriteLockCoverage([new WorkingCopyWriteLock("sub", 0)]);

        await Assert.That(coverage.Any).IsTrue();
        await Assert.That(coverage.Reaches(NodeKind.Directory, "sub")).IsTrue();
    }

    /// <summary>
    /// Depth zero is the whole reason the column exists as a number rather than a flag: it locks one
    /// directory and leaves its children alone.
    /// </summary>
    [Test]
    public async Task A_lock_at_depth_zero_does_not_reach_the_directory_below_it()
    {
        var coverage = new WriteLockCoverage([new WorkingCopyWriteLock("sub", 0)]);

        await Assert.That(coverage.Reaches(NodeKind.Directory, "sub/deep")).IsFalse();
    }

    /// <summary>
    /// The boundary, both sides, measured on 1.8.15: a lock on <c>sub</c> at one level made
    /// <c>svn status</c> print <c>L</c> on <c>sub</c> and <c>sub/deep</c> and not on
    /// <c>sub/deep/deeper</c>. So the count is levels below the locked directory, not a path depth.
    /// </summary>
    [Test]
    [Arguments("sub", true)]
    [Arguments("sub/deep", true)]
    [Arguments("sub/deep/deeper", false)]
    public async Task A_lock_at_one_level_reaches_exactly_one_level_down(
        string directory,
        bool expected
    )
    {
        var coverage = new WriteLockCoverage([new WorkingCopyWriteLock("sub", 1)]);

        await Assert.That(coverage.Reaches(NodeKind.Directory, directory)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("sub")]
    [Arguments("sub/deep")]
    [Arguments("sub/deep/deeper")]
    public async Task A_lock_at_every_level_reaches_however_deep_the_tree_goes(string directory)
    {
        var coverage = new WriteLockCoverage([new WorkingCopyWriteLock("sub", EveryLevel)]);

        await Assert.That(coverage.Reaches(NodeKind.Directory, directory)).IsTrue();
    }

    /// <summary>
    /// The row a killed client actually leaves: the root, at every level. Measured by killing an
    /// <c>svn commit</c> mid-flight, which left <c>('', -1)</c> and nothing else.
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("art")]
    [Arguments("sub/deep/deeper")]
    public async Task The_root_locked_at_every_level_reaches_the_whole_working_copy(
        string directory
    )
    {
        var coverage = new WriteLockCoverage([new WorkingCopyWriteLock(string.Empty, EveryLevel)]);

        await Assert.That(coverage.Reaches(NodeKind.Directory, directory)).IsTrue();
    }

    /// <summary>
    /// The root is the empty string, so it prefixes every path without a separator of its own.
    /// Getting this wrong makes a depth-zero root lock cover the entire tree.
    /// </summary>
    [Test]
    [Arguments("", true)]
    [Arguments("art", false)]
    [Arguments("sub/deep", false)]
    public async Task The_root_locked_at_depth_zero_reaches_only_the_root(
        string directory,
        bool expected
    )
    {
        var coverage = new WriteLockCoverage([new WorkingCopyWriteLock(string.Empty, 0)]);

        await Assert.That(coverage.Reaches(NodeKind.Directory, directory)).IsEqualTo(expected);
    }

    [Test]
    public async Task The_root_locked_at_one_level_reaches_its_children_and_no_further()
    {
        var coverage = new WriteLockCoverage([new WorkingCopyWriteLock(string.Empty, 1)]);

        await Assert.That(coverage.Reaches(NodeKind.Directory, "art")).IsTrue();
        await Assert.That(coverage.Reaches(NodeKind.Directory, "sub/deep")).IsFalse();
    }

    /// <summary>
    /// A prefix is not an ancestor. Without the separator, a lock on <c>sub</c> would silently claim
    /// <c>subtree</c> — a sibling directory that shares its first three letters.
    /// </summary>
    [Test]
    [Arguments("subtree")]
    [Arguments("subtree/deep")]
    public async Task A_directory_that_merely_starts_with_a_locked_ones_name_is_not_reached(
        string directory
    )
    {
        var coverage = new WriteLockCoverage([new WorkingCopyWriteLock("sub", EveryLevel)]);

        await Assert.That(coverage.Reaches(NodeKind.Directory, directory)).IsFalse();
    }

    /// <summary>
    /// Only directories are ever locked — wc.db keeps <c>local_dir_relpath</c>, and <c>svn
    /// status</c> prints <c>L</c> against directories only. A file's path sits under a locked
    /// directory exactly the way a locked subdirectory's does, so deciding on the path alone marks
    /// every file in a wedged working copy as locked.
    /// </summary>
    [Test]
    [Arguments(NodeKind.File)]
    [Arguments(NodeKind.Symlink)]
    [Arguments(NodeKind.Unknown)]
    public async Task Nothing_but_a_directory_is_ever_reached(NodeKind kind)
    {
        var coverage = new WriteLockCoverage([new WorkingCopyWriteLock(string.Empty, EveryLevel)]);

        await Assert.That(coverage.Reaches(kind, "art/hero.png")).IsFalse();
        await Assert.That(coverage.Reaches(NodeKind.Directory, "art/hero.png")).IsTrue();
    }

    [Test]
    public async Task Any_one_of_several_locks_is_enough_to_reach_a_directory()
    {
        var coverage = new WriteLockCoverage([
            new WorkingCopyWriteLock("art", 0),
            new WorkingCopyWriteLock("sub", EveryLevel),
        ]);

        await Assert.That(coverage.Reaches(NodeKind.Directory, "art")).IsTrue();
        await Assert.That(coverage.Reaches(NodeKind.Directory, "sub/deep")).IsTrue();
        await Assert.That(coverage.Reaches(NodeKind.Directory, "art/textures")).IsFalse();
        await Assert.That(coverage.Reaches(NodeKind.Directory, "other")).IsFalse();
    }
}
