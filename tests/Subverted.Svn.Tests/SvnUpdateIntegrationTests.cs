namespace Subverted.Svn.Tests;

/// <summary>
/// <c>svn update</c> against a real repository, with a second checkout standing in for the rest of
/// the studio. These pin the one thing about update that no amount of reading the documentation
/// settles: <b>it exits zero on a conflict</b>, so the exit code says the client ran and nothing
/// more, and every case below is the difference between that and a clean update.
/// </summary>
public sealed class SvnUpdateIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [Test]
    public async Task An_update_brings_a_teammates_commit_in_and_reports_the_revision()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Write("src/a.txt", "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "their work");

        var outcome = await new SvnUpdateCommand(Svn).UpdateAsync(copy.Root, copy.Root, None);

        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Conflicts).IsEqualTo(0);
        await Assert.That(outcome.SkippedPaths).IsEqualTo(0);
        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt"))).IsEqualTo("theirs\n");
    }

    /// <summary>
    /// SVN says "At revision N" rather than "Updated to revision N" when nothing came down, and it
    /// is the same revision either way. Reading only the first wording reports no revision at all
    /// for every working copy that is already current, which is most of them most of the time.
    /// </summary>
    [Test]
    public async Task An_update_with_nothing_to_bring_still_reports_where_the_working_copy_stands()
    {
        using var copy = OneCommit();

        var outcome = await new SvnUpdateCommand(Svn).UpdateAsync(copy.Root, copy.Root, None);

        await Assert.That(outcome.Revision).IsEqualTo(1L);
        await Assert.That(outcome.Conflicts).IsEqualTo(0);
    }

    /// <summary>
    /// The rule the whole design turns on. SVN merges what it can, leaves markers in the file, and
    /// <b>exits zero</b> — so a caller that trusts the exit code hands somebody a working copy full
    /// of <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> and calls it up to date.
    /// </summary>
    [Test]
    public async Task A_text_conflict_is_counted_although_the_client_reports_success()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Write("src/a.txt", "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "their work");
        copy.Write("src/a.txt", "mine\n");

        var outcome = await new SvnUpdateCommand(Svn).UpdateAsync(copy.Root, copy.Root, None);

        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Conflicts).IsEqualTo(1);
        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt"))).Contains("<<<<<<<");
    }

    /// <summary>
    /// Postponing is stated rather than left to <c>--non-interactive</c>'s default, because the
    /// alternative is a daemon picking a side of somebody's conflict. The evidence that it was
    /// postponed and not resolved is the pair of files SVN leaves beside the conflicted one.
    /// </summary>
    [Test]
    public async Task A_conflict_is_postponed_rather_than_resolved_for_the_user()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Write("src/a.txt", "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "their work");
        copy.Write("src/a.txt", "mine\n");

        await new SvnUpdateCommand(Svn).UpdateAsync(copy.Root, copy.Root, None);

        await Assert.That(File.Exists(copy.Absolute("src/a.txt.mine"))).IsTrue();
        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt.mine"))).IsEqualTo("mine\n");
    }

    [Test]
    public async Task A_property_conflict_is_counted_the_same_way_a_text_one_is()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Svn("propset", "--quiet", "custom:x", "theirs", "src/a.txt");
        teammate.Svn("commit", "--quiet", "-m", "their property");
        copy.Svn("propset", "--quiet", "custom:x", "mine", "src/a.txt");

        var outcome = await new SvnUpdateCommand(Svn).UpdateAsync(copy.Root, copy.Root, None);

        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Conflicts).IsEqualTo(1);
    }

    /// <summary>
    /// A tree conflict blocks the change, so SVN prints "At revision N" — the same wording as an
    /// update that had nothing to do. The count is the only thing that tells them apart.
    /// </summary>
    [Test]
    public async Task A_tree_conflict_is_counted_although_nothing_came_down()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Svn("delete", "--quiet", "src/a.txt");
        teammate.Svn("commit", "--quiet", "-m", "their deletion");
        copy.Write("src/a.txt", "still working on this\n");

        var outcome = await new SvnUpdateCommand(Svn).UpdateAsync(copy.Root, copy.Root, None);

        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Conflicts).IsEqualTo(1);
    }

    /// <summary>
    /// A path SVN was never going to touch is <em>skipped</em> and the command still exits zero, so
    /// a mistyped target looks exactly like a successful update. That is why the count is modelled
    /// separately from conflicts rather than folded into them or dropped.
    /// </summary>
    [Test]
    public async Task A_path_outside_the_working_copy_is_skipped_rather_than_refused()
    {
        using var copy = OneCommit();
        var outside = Path.Combine(copy.RepositoryPath, "conf");

        var outcome = await new SvnUpdateCommand(Svn).UpdateAsync(copy.Root, outside, None);

        await Assert.That(outcome.SkippedPaths).IsEqualTo(1);
        await Assert.That(outcome.Conflicts).IsEqualTo(0);
        await Assert.That(outcome.Revision).IsNull();
    }

    /// <summary>
    /// Updating one path leaves the rest of the working copy where it was — the property `sv up
    /// PATH` is for, and the reason the target is passed through at all.
    /// </summary>
    [Test]
    public async Task Updating_one_path_leaves_the_rest_of_the_tree_behind()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Write("src/a.txt", "theirs\n");
        teammate.Write("art/hero.txt", "theirs too\n");
        teammate.Svn("commit", "--quiet", "-m", "both");

        await new SvnUpdateCommand(Svn).UpdateAsync(copy.Root, copy.Absolute("src"), None);

        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt"))).IsEqualTo("theirs\n");
        await Assert.That(File.ReadAllText(copy.Absolute("art/hero.txt"))).IsEqualTo("ours\n");
    }

    /// <summary>
    /// The one way an update really fails: the server is not there. It has to throw rather than
    /// come back with a revision of null, because "nothing to bring" and "could not ask" are
    /// opposite answers and a front-end acts differently on each.
    /// </summary>
    [Test]
    public async Task An_update_that_cannot_reach_the_repository_fails_with_what_svn_said()
    {
        using var copy = OneCommit();
        Directory.Move(copy.RepositoryPath, copy.RepositoryPath + "-gone");

        await Assert
            .That(async () =>
                await new SvnUpdateCommand(Svn).UpdateAsync(copy.Root, copy.Root, None)
            )
            .Throws<SvnCommandException>()
            .WithMessageContaining("Unable to connect to a repository");
    }

    /// <summary>
    /// SVN spells its notification paths with the platform's separator, and every RelPath Subverted
    /// reports uses a slash. Left alone, <c>sv up</c> and <c>sv st</c> name one file two ways.
    /// </summary>
    [Test]
    public async Task The_paths_svn_announces_are_spelled_the_way_status_spells_them()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Write("src/a.txt", "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "their work");

        var outcome = await new SvnUpdateCommand(Svn).UpdateAsync(copy.Root, copy.Root, None);

        await Assert.That(outcome.Notifications).Contains("src/a.txt");
        await Assert.That(outcome.Notifications).DoesNotContain(@"src\a.txt");
    }

    private static SvnWorkingCopy OneCommit()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("src/a.txt", "ours\n");
            copy.Write("art/hero.txt", "ours\n");
            copy.Svn("add", "--quiet", "src", "art");
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
