namespace Subverted.Svn.Tests;

/// <summary>
/// The shell-out path (D6) against a real repository. What no unit test can establish is that the
/// arguments we pass mean what we think they mean to the client that is actually installed.
/// </summary>
public sealed class SvnCommandIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [Test]
    public async Task A_history_comes_back_newest_first()
    {
        using var copy = ThreeCommits();

        var revisions = await new SvnLogCommand(Svn).ReadAsync(
            copy.Root,
            copy.Root,
            null,
            null,
            None
        );

        await Assert
            .That(revisions.Select(revision => revision.Message))
            .IsEquivalentTo(["third", "second", "first"]);
        await Assert
            .That(revisions.Select(revision => revision.Revision))
            .IsEquivalentTo([3L, 2L, 1L]);
    }

    /// <summary>A limit takes the newest, not the first — the opposite would be quietly useless.</summary>
    [Test]
    public async Task A_limit_keeps_the_newest_revisions_and_drops_the_rest()
    {
        using var copy = ThreeCommits();

        var revisions = await new SvnLogCommand(Svn).ReadAsync(copy.Root, copy.Root, 2, null, None);

        await Assert.That(revisions.Select(revision => revision.Revision)).IsEquivalentTo([3L, 2L]);
    }

    [Test]
    public async Task Every_revision_read_from_a_real_repository_carries_a_date()
    {
        using var copy = ThreeCommits();

        var revisions = await new SvnLogCommand(Svn).ReadAsync(
            copy.Root,
            copy.Root,
            null,
            null,
            None
        );

        await Assert.That(revisions.Where(revision => revision.Date is null)).IsEmpty();
    }

    /// <summary>
    /// <c>--verbose</c> is what makes the changed paths arrive at all, and a copy is the shape
    /// that carries the most of them.
    /// </summary>
    [Test]
    public async Task A_copy_arrives_with_the_path_it_was_copied_from()
    {
        using var copy = ThreeCommits();

        var newest = (await new SvnLogCommand(Svn).ReadAsync(copy.Root, copy.Root, 1, null, None))[
            0
        ];

        var added = newest.ChangedPaths.Single(changed => changed.Path.EndsWith("/b.txt"));
        await Assert.That(added.Change).IsEqualTo(Core.PathChange.Added);
        await Assert.That(added.CopiedFromPath).EndsWith("/a.txt");
        await Assert.That(added.CopiedFromRevision).IsEqualTo(2L);
    }

    [Test]
    public async Task A_local_edit_comes_back_as_a_unified_diff_of_itself()
    {
        using var copy = ThreeCommits();
        copy.Write("src/a.txt", "one\ntwo\nthree\n");

        var diff = await new SvnDiffCommand(Svn).ReadAsync(copy.Root, copy.Root, None);

        await Assert.That(diff).Contains("+three");
        await Assert.That(diff).Contains("@@");
        await Assert.That(diff).Contains("Index: src/a.txt");
    }

    /// <summary>
    /// SVN translates its own output. Without the C locale this reads <c>(kopia robocza)</c> on
    /// the machine this was written on, and every prefix a front-end matches on becomes a guess
    /// about which language the studio installed.
    /// </summary>
    [Test]
    public async Task Svns_output_is_in_one_language_whatever_the_machine_is_set_to()
    {
        using var copy = ThreeCommits();
        copy.Write("src/a.txt", "one\ntwo\nthree\n");

        var diff = await new SvnDiffCommand(Svn).ReadAsync(copy.Root, copy.Root, None);

        await Assert.That(diff).Contains("(working copy)");
        await Assert.That(diff).Contains("(revision 3)");
    }

    /// <summary>
    /// svn writes the paths in a diff's headers in its console's code page, not in UTF-8: Polish
    /// letters came back as replacement characters, and a Japanese name as question marks nothing
    /// could turn back into the name. The file is added through its folder because a Japanese name
    /// given as an argument is refused (E200009) even from a UTF-8 console — that is D33's open end.
    /// </summary>
    [Test]
    [Arguments("names/zażółć.txt")]
    [Arguments("names/ドラゴン.txt")]
    public async Task A_name_outside_ascii_diffs_as_itself(string name)
    {
        using var copy = SvnWorkingCopy.Create();
        copy.Write(name, "one\n");
        copy.Svn("add", "--quiet", "names");
        copy.Svn("commit", "--quiet", "-m", "a name outside ascii");
        copy.Write(name, "two\n");

        var diff = await new SvnDiffCommand(Svn).ReadAsync(copy.Root, copy.Root, None);

        await Assert.That(diff).Contains($"Index: {name}");
        await Assert.That(diff).Contains($"--- {name}\t(revision 1)");
        await Assert.That(diff).Contains($"+++ {name}\t(working copy)");
    }

    [Test]
    public async Task A_working_copy_with_nothing_changed_diffs_to_nothing()
    {
        using var copy = ThreeCommits();

        var diff = await new SvnDiffCommand(Svn).ReadAsync(copy.Root, copy.Root, None);

        await Assert.That(diff).IsEmpty();
    }

    [Test]
    public async Task A_path_svn_refuses_fails_with_what_svn_said_about_it()
    {
        using var copy = ThreeCommits();
        var missing = copy.Absolute("src/never-added.txt");

        await Assert
            .That(async () => await new SvnDiffCommand(Svn).ReadAsync(copy.Root, missing, None))
            .Throws<SvnCommandException>()
            .WithMessageContaining("is not under version control");
    }

    [Test]
    public async Task A_log_for_a_path_svn_refuses_fails_rather_than_coming_back_empty()
    {
        using var copy = ThreeCommits();
        var missing = copy.Absolute("src/never-added.txt");

        await Assert
            .That(async () =>
                await new SvnLogCommand(Svn).ReadAsync(copy.Root, missing, null, null, None)
            )
            .Throws<SvnCommandException>();
    }

    /// <summary>
    /// The studio machine without <c>svn</c> installed. It has to say so, because the advice is
    /// to install it — not to restart the daemon.
    /// </summary>
    [Test]
    public async Task A_client_that_is_not_installed_says_so_rather_than_crashing()
    {
        var absent = new SvnCommand("svn-subverted-does-not-ship-this");

        await Assert
            .That(async () => await absent.RunAsync(Path.GetTempPath(), ["--version"], None))
            .Throws<SvnCommandException>()
            .WithMessageContaining("svn-subverted-does-not-ship-this");
    }

    private static SvnWorkingCopy ThreeCommits()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("src/a.txt", "one\n");
            copy.Svn("add", "--quiet", "src");
            copy.Svn("commit", "--quiet", "-m", "first");

            // svn commit leaves the root at the revision before it, and the next commit on a path
            // under an out-of-date root is refused.
            copy.Svn("update", "--quiet");

            copy.Write("src/a.txt", "one\ntwo\n");
            copy.Svn("commit", "--quiet", "-m", "second");
            copy.Svn("update", "--quiet");

            copy.Svn("copy", "--quiet", "src/a.txt", "src/b.txt");
            copy.Svn("commit", "--quiet", "-m", "third");
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
