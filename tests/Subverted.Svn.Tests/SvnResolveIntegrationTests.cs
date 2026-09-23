using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// <c>svn resolve</c> against a real repository, with a second checkout standing in for the rest of
/// the studio. These pin the two things about resolve that reading the documentation does not
/// settle: <b>its default depth is empty</b>, so naming a directory resolves nothing while exiting
/// zero, and <b>every per-path failure is a warning</b> rather than a thrown error — including the
/// one an artist hits by leaving the file open in the application that owns it.
/// </summary>
public sealed class SvnResolveIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [Test]
    public async Task Keeping_mine_leaves_this_working_copys_bytes_and_clears_the_conflict()
    {
        using var copy = Conflicted(out var teammate);
        using var _ = teammate;

        var outcome = await Resolve(copy, ConflictResolution.Mine);

        await Assert.That(outcome.ResolvedPaths).IsEquivalentTo(new[] { "src/a.txt" });
        await Assert.That(outcome.Refusals).IsEmpty();
        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt"))).IsEqualTo("mine\n");
    }

    [Test]
    public async Task Keeping_theirs_takes_the_incoming_bytes()
    {
        using var copy = Conflicted(out var teammate);
        using var _ = teammate;

        var outcome = await Resolve(copy, ConflictResolution.Theirs);

        await Assert.That(outcome.ResolvedPaths).IsEquivalentTo(new[] { "src/a.txt" });
        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt"))).IsEqualTo("theirs\n");
    }

    [Test]
    public async Task Keeping_base_takes_the_revision_both_sides_started_from()
    {
        using var copy = Conflicted(out var teammate);
        using var _ = teammate;

        var outcome = await Resolve(copy, ConflictResolution.Base);

        await Assert.That(outcome.ResolvedPaths).IsEquivalentTo(new[] { "src/a.txt" });
        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt"))).IsEqualTo("ours\n");
    }

    /// <summary>
    /// The trap the front-end has to warn about. SVN does not read the file to check: keeping the
    /// working version marks a node resolved with the merge markers still in it, and the next
    /// commit ships <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> to the whole studio.
    /// </summary>
    [Test]
    public async Task Keeping_the_working_version_marks_it_resolved_with_the_markers_still_there()
    {
        using var copy = Conflicted(out var teammate);
        using var _ = teammate;

        var outcome = await Resolve(copy, ConflictResolution.Working);

        await Assert.That(outcome.ResolvedPaths).IsEquivalentTo(new[] { "src/a.txt" });
        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt"))).Contains("<<<<<<<");
    }

    /// <summary>
    /// The reason <c>--recursive</c> is passed on every invocation. Resolve's own default depth is
    /// <c>empty</c>, so a directory target resolves nothing, prints nothing and exits zero — a
    /// "success" that leaves every conflict exactly where it was.
    /// </summary>
    [Test]
    public async Task A_directory_target_reaches_the_conflicts_underneath_it()
    {
        using var copy = Conflicted(out var teammate);
        using var _ = teammate;

        var outcome = await new SvnResolveCommand(Svn).ResolveAsync(
            copy.Root,
            [copy.Root],
            ConflictResolution.Mine,
            None
        );

        await Assert.That(outcome.ResolvedPaths).IsEquivalentTo(new[] { "src/a.txt" });
    }

    /// <summary>
    /// Nothing to do is a real answer and not a failure, which is only safe to say because the
    /// recursion above means "no lines" can no longer mean "did not look".
    /// </summary>
    [Test]
    public async Task A_working_copy_with_no_conflicts_resolves_nothing_and_refuses_nothing()
    {
        using var copy = OneCommit();

        var outcome = await new SvnResolveCommand(Svn).ResolveAsync(
            copy.Root,
            [copy.Root],
            ConflictResolution.Mine,
            None
        );

        await Assert.That(outcome.ResolvedPaths).IsEmpty();
        await Assert.That(outcome.Refusals).IsEmpty();
    }

    /// <summary>
    /// A tree conflict takes only the working version. Asked for anything else SVN refuses it and
    /// leaves the node exactly as conflicted as it was — so this must not read as done.
    /// </summary>
    [Test]
    [Arguments(ConflictResolution.Theirs)]
    [Arguments(ConflictResolution.Mine)]
    [Arguments(ConflictResolution.Base)]
    public async Task A_tree_conflict_refuses_every_version_but_the_working_one(
        ConflictResolution resolution
    )
    {
        using var copy = TreeConflicted(out var teammate);
        using var _ = teammate;

        var outcome = await Resolve(copy, resolution);

        await Assert.That(outcome.ResolvedPaths).IsEmpty();
        await Assert.That(outcome.Refusals.Count).IsEqualTo(1);
        await Assert.That(outcome.Refusals[0]).Contains("W155027");
    }

    [Test]
    public async Task A_tree_conflict_does_resolve_to_the_working_version()
    {
        using var copy = TreeConflicted(out var teammate);
        using var _ = teammate;

        var outcome = await Resolve(copy, ConflictResolution.Working);

        await Assert.That(outcome.ResolvedPaths).IsEquivalentTo(new[] { "src/a.txt" });
        await Assert.That(outcome.Refusals).IsEmpty();
    }

    /// <summary>
    /// Unlike <c>svn unlock</c>, which validates every target first and releases none of them if
    /// one is wrong, resolve is per-path: it settles the good targets and reports the bad one. A
    /// caller that treated the refusal as "nothing happened" would be wrong about both.
    /// </summary>
    [Test]
    public async Task A_bad_target_is_refused_and_the_other_targets_are_still_resolved()
    {
        using var copy = Conflicted(out var teammate);
        using var _ = teammate;

        var outcome = await new SvnResolveCommand(Svn).ResolveAsync(
            copy.Root,
            [copy.Absolute("src/a.txt"), copy.Absolute("src/nosuch.txt")],
            ConflictResolution.Mine,
            None
        );

        await Assert.That(outcome.ResolvedPaths).IsEquivalentTo(new[] { "src/a.txt" });
        await Assert.That(outcome.Refusals.Count).IsEqualTo(1);
        await Assert.That(outcome.Refusals[0]).Contains("W155010");
        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt"))).IsEqualTo("mine\n");
    }

    /// <summary>
    /// A path in no working copy is a refusal like the rest — SVN reports it as a warning and not
    /// as the client failing, so it must not throw.
    /// </summary>
    [Test]
    public async Task A_path_in_no_working_copy_is_refused_rather_than_thrown()
    {
        using var copy = Conflicted(out var teammate);
        using var _ = teammate;

        var outcome = await new SvnResolveCommand(Svn).ResolveAsync(
            copy.Root,
            [copy.RepositoryPath],
            ConflictResolution.Mine,
            None
        );

        await Assert.That(outcome.ResolvedPaths).IsEmpty();
        await Assert.That(outcome.Refusals.Count).IsEqualTo(1);
        await Assert.That(outcome.Refusals[0]).Contains("W155007");
    }

    /// <summary>
    /// A property conflict reports itself resolved in exactly the same words as a text one, which
    /// is what lets a single parser cover all three kinds.
    /// </summary>
    [Test]
    public async Task A_property_conflict_resolves_and_says_so_the_same_way()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Svn("propset", "--quiet", "svn:eol-style", "native", "src/a.txt");
        teammate.Svn("commit", "--quiet", "-m", "their property");
        copy.Svn("propset", "--quiet", "svn:eol-style", "CRLF", "src/a.txt");
        copy.Svn("update", "--quiet", "--accept", "postpone");

        var outcome = await Resolve(copy, ConflictResolution.Working);

        await Assert.That(outcome.ResolvedPaths).IsEquivalentTo(new[] { "src/a.txt" });
        await Assert.That(outcome.Refusals).IsEmpty();
    }

    /// <summary>
    /// SVN leaves <c>.mine</c> and <c>.rN</c> beside a conflicted file and clears them on resolve.
    /// They are unversioned, so anything left behind shows up as <c>?</c> in every later
    /// <c>sv st</c> and teaches people to ignore the listing.
    /// </summary>
    [Test]
    public async Task Resolving_clears_the_files_svn_left_beside_the_conflicted_one()
    {
        using var copy = Conflicted(out var teammate);
        using var _ = teammate;
        await Assert.That(File.Exists(copy.Absolute("src/a.txt.mine"))).IsTrue();

        await Resolve(copy, ConflictResolution.Mine);

        // Not a `a.txt.*` glob: Win32 wildcards match an empty extension, so it finds a.txt itself.
        var leftovers = Directory
            .EnumerateFiles(copy.Absolute("src"))
            .Select(Path.GetFileName)
            .Where(name => name!.StartsWith("a.txt.", StringComparison.Ordinal));

        await Assert.That(leftovers).IsEmpty();
    }

    [Test]
    public async Task The_paths_svn_announces_are_spelled_the_way_status_spells_them()
    {
        using var copy = Conflicted(out var teammate);
        using var _ = teammate;

        var outcome = await Resolve(copy, ConflictResolution.Mine);

        await Assert.That(outcome.ResolvedPaths[0]).IsEqualTo("src/a.txt");
    }

    /// <summary>
    /// The case a studio hits by accident: the artist still has the asset open in the application
    /// that owns it, so SVN cannot write over it. It is a refusal rather than a thrown error — and
    /// it leaves the working copy needing <c>svn cleanup</c>, which is why SVN's own wording is
    /// passed through instead of being re-worded into something that does not say so.
    /// </summary>
    [Test]
    public async Task A_file_another_process_holds_open_is_refused_and_says_to_run_cleanup()
    {
        if (!OperatingSystem.IsWindows())
        {
            // POSIX does not enforce a share mode, so there is no way to hold the file against SVN
            // and nothing to assert. The Windows side is the one a studio runs on.
            return;
        }

        using var copy = Conflicted(out var teammate);
        using var _ = teammate;

        ResolveOutcome outcome;
        using (
            File.Open(
                copy.Absolute("src/a.txt"),
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            )
        )
        {
            outcome = await Resolve(copy, ConflictResolution.Theirs);
        }

        await Assert.That(outcome.ResolvedPaths).IsEmpty();
        await Assert.That(outcome.Refusals.Count).IsEqualTo(1);
        await Assert.That(outcome.Refusals[0]).Contains("W155009");

        // Left wedged: every later command in this working copy fails until cleanup runs.
        copy.Svn("cleanup");
    }

    private static Task<ResolveOutcome> Resolve(
        SvnWorkingCopy copy,
        ConflictResolution resolution
    ) =>
        new SvnResolveCommand(Svn).ResolveAsync(
            copy.Root,
            [copy.Absolute("src/a.txt")],
            resolution,
            None
        );

    /// <summary>A text conflict on <c>src/a.txt</c>: ours at BASE, theirs at r2, mine on disk.</summary>
    private static SvnWorkingCopy Conflicted(out SvnWorkingCopy teammate)
    {
        var copy = OneCommit();
        try
        {
            teammate = copy.AnotherCheckout();
            teammate.Write("src/a.txt", "theirs\n");
            teammate.Svn("commit", "--quiet", "-m", "their work");
            copy.Write("src/a.txt", "mine\n");
            copy.Svn("update", "--quiet", "--accept", "postpone");
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    /// <summary>Local edit against an incoming delete — the tree conflict a studio actually hits.</summary>
    private static SvnWorkingCopy TreeConflicted(out SvnWorkingCopy teammate)
    {
        var copy = OneCommit();
        try
        {
            teammate = copy.AnotherCheckout();
            teammate.Svn("delete", "--quiet", "src/a.txt");
            teammate.Svn("commit", "--quiet", "-m", "their delete");
            copy.Write("src/a.txt", "mine\n");
            copy.Svn("update", "--quiet", "--accept", "postpone");
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    private static SvnWorkingCopy OneCommit()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("src/a.txt", "ours\n");
            copy.Svn("add", "--quiet", "src");
            copy.Svn("commit", "--quiet", "-m", "first");
            copy.Svn("update", "--quiet");
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }
}
