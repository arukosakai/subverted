using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// D1's fallback against a real repository. The parser tests pin what the document means; what only
/// running it can settle is that the arguments fetch that document at all, and that the answer is
/// the same one the fast path gives for the same tree.
/// </summary>
public sealed class SvnFallbackIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    /// <summary>
    /// The whole point of the fallback: the daemon above it cannot tell which path answered. A
    /// difference here is a working copy that reads differently depending on which SVN the studio
    /// has installed.
    /// </summary>
    [Test]
    public async Task The_client_and_wc_db_answer_the_same_for_every_node_of_a_real_working_copy()
    {
        using var copy = EveryShape();

        var throughClient = await new SvnStatusCommand(Svn).ReadAsync(copy.Root, None);
        using var reader = WcDbReader.Open(copy.Root);
        var throughWcDb = new WorkingCopyScanner(reader, []).Scan();

        await Assert.That(Comparable(throughClient)).IsEquivalentTo(Comparable(throughWcDb));
    }

    /// <summary>
    /// The two shapes where the readers used to disagree, now pinned to exact values on both sides
    /// rather than to each other — equality alone would stay green if both regressed together.
    /// A locally deleted node keeps the BASE revision it is being deleted from; a copy has no
    /// working revision at all, and must not report its source's.
    /// </summary>
    [Test]
    public async Task A_deleted_node_keeps_its_revision_and_a_copy_has_none_on_both_paths()
    {
        using var copy = EveryShape();

        var throughClient = await new SvnStatusCommand(Svn).ReadAsync(copy.Root, None);
        using var reader = WcDbReader.Open(copy.Root);
        var throughWcDb = new WorkingCopyScanner(reader, []).Scan();

        await Assert.That(Revision(throughClient, "src/deleted.txt")).IsEqualTo(1L);
        await Assert.That(Revision(throughWcDb, "src/deleted.txt")).IsEqualTo(1L);
        await Assert.That(Revision(throughClient, "src/copied.txt")).IsNull();
        await Assert.That(Revision(throughWcDb, "src/copied.txt")).IsNull();

        // The negative case: a node with no local layer must still report its revision, or
        // "read BASE" could be satisfied by reporting nothing anywhere.
        await Assert.That(Revision(throughWcDb, "src/clean.txt")).IsEqualTo(1L);
    }

    /// <summary>
    /// <c>--verbose</c> is what carries the unmodified nodes, and the daemon holds a whole working
    /// copy to filter per request. Without it the fallback could not answer <c>sv st -v</c> at all.
    /// </summary>
    [Test]
    public async Task Unmodified_nodes_are_read_and_not_only_the_changed_ones()
    {
        using var copy = EveryShape();

        var entries = await new SvnStatusCommand(Svn).ReadAsync(copy.Root, None);

        await Assert.That(Status(entries, "src/clean.txt")).IsEqualTo(NodeStatus.Unmodified);
    }

    [Test]
    public async Task Ignored_nodes_are_read_and_reported_as_ignored_rather_than_dropped()
    {
        using var copy = EveryShape();

        var entries = await new SvnStatusCommand(Svn).ReadAsync(copy.Root, None);

        await Assert.That(Status(entries, "build.tmp")).IsEqualTo(NodeStatus.Ignored);
        await Assert.That(Status(entries, "stray.txt")).IsEqualTo(NodeStatus.Unversioned);
    }

    /// <summary>
    /// The client echoes the target it is given, so it is run in the root against <c>.</c> — and
    /// an absolute path in every entry would be a status listing nobody can read.
    /// </summary>
    [Test]
    public async Task Paths_come_back_relative_to_the_root_with_the_root_itself_as_the_empty_one()
    {
        using var copy = EveryShape();

        var entries = await new SvnStatusCommand(Svn).ReadAsync(copy.Root, None);

        await Assert.That(entries.Select(entry => entry.RelPath)).Contains(string.Empty);
        await Assert.That(entries.Select(entry => entry.RelPath)).Contains("src/clean.txt");
        await Assert.That(entries.Where(entry => Path.IsPathRooted(entry.RelPath))).IsEmpty();
    }

    /// <summary>
    /// Worth knowing before trusting this on its own: outside a working copy <c>svn status</c>
    /// warns, exits zero and writes an empty document — which reads as "everything is clean". That
    /// is why the fallback asks <c>svn info</c> first and lets it be the thing that refuses.
    /// </summary>
    [Test]
    public async Task A_directory_that_is_no_working_copy_reads_as_empty_rather_than_failing()
    {
        var directory = Directory.CreateTempSubdirectory("subverted-notawc-");
        try
        {
            var entries = await new SvnStatusCommand(Svn).ReadAsync(directory.FullName, None);

            await Assert.That(entries).IsEmpty();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The limit of the fallback, stated: it answers for a database this build refuses to read, not
    /// for one Subversion cannot read either. A corrupt wc.db fails both paths, and failing is the
    /// only honest answer — the alternative is an empty listing that reads as a clean tree.
    /// </summary>
    [Test]
    public async Task A_working_copy_svn_cannot_read_either_fails_rather_than_reading_as_clean()
    {
        using var copy = EveryShape();
        copy.Write(".svn/wc.db", "this is not a database");

        await Assert
            .That(async () => await new SvnStatusCommand(Svn).ReadAsync(copy.Root, None))
            .Throws<SvnCommandException>()
            .WithMessageContaining("corrupt");
    }

    /// <summary>
    /// The fallback's first question. Both readings come from the same working copy, so a
    /// disagreement means one of them is describing a different one.
    /// </summary>
    [Test]
    public async Task The_client_and_wc_db_name_the_same_root_repository_and_uuid()
    {
        using var copy = EveryShape();

        var throughClient = await new SvnInfoCommand(Svn).ReadAsync(copy.Root, None);
        using var reader = WcDbReader.Open(copy.Root);

        await Assert.That(throughClient.RootPath).IsEqualTo(Path.GetFullPath(reader.Info.RootPath));
        await Assert.That(throughClient.RepositoryRoot).IsEqualTo(reader.Info.RepositoryRoot);
        await Assert.That(throughClient.RepositoryUuid).IsEqualTo(reader.Info.RepositoryUuid);
    }

    /// <summary>
    /// A file is a legitimate thing to ask <c>sv st</c> about, and the client has to be run in a
    /// directory. The answer is still the working copy's, not the file's.
    /// </summary>
    [Test]
    public async Task A_file_inside_the_working_copy_answers_with_the_working_copys_root()
    {
        using var copy = EveryShape();

        var info = await new SvnInfoCommand(Svn).ReadAsync(copy.Absolute("src/clean.txt"), None);

        await Assert.That(info.RootPath).IsEqualTo(Path.GetFullPath(copy.Root));
    }

    [Test]
    public async Task A_path_in_no_working_copy_fails_rather_than_inventing_a_root()
    {
        await Assert
            .That(async () => await new SvnInfoCommand(Svn).ReadAsync(Path.GetTempPath(), None))
            .Throws<SvnCommandException>();
    }

    /// <summary>
    /// The two states that used to fail the fallback outright. Kept apart from
    /// <see cref="EveryShape"/> because an external has to be committed and updated, which moves
    /// every other node's revision and would quietly rewrite the assertions above.
    /// </summary>
    [Test]
    public async Task An_obstruction_and_an_external_read_the_same_on_both_paths()
    {
        using var copy = WithObstructionAndExternal();

        var throughClient = await new SvnStatusCommand(Svn).ReadAsync(copy.Root, None);
        using var reader = WcDbReader.Open(copy.Root);
        var throughWcDb = new WorkingCopyScanner(reader, []).Scan();

        await Assert.That(Comparable(throughClient)).IsEquivalentTo(Comparable(throughWcDb));

        foreach (var entries in new[] { throughClient, throughWcDb })
        {
            await Assert.That(Status(entries, "file-now-dir")).IsEqualTo(NodeStatus.Obstructed);
            await Assert.That(Status(entries, "dir-now-file")).IsEqualTo(NodeStatus.Obstructed);
            await Assert.That(Status(entries, "ext")).IsEqualTo(NodeStatus.External);
            await Assert.That(Revision(entries, "ext")).IsNull();
        }
    }

    /// <summary>
    /// An external's contents belong to another working copy. <c>svn status</c> recurses into it
    /// and lists them; neither reader may fold them into this listing, and the external must not
    /// read as an unversioned directory waiting to be added.
    /// </summary>
    [Test]
    public async Task Neither_path_reports_anything_inside_an_external()
    {
        using var copy = WithObstructionAndExternal();

        var throughClient = await new SvnStatusCommand(Svn).ReadAsync(copy.Root, None);
        using var reader = WcDbReader.Open(copy.Root);
        var throughWcDb = new WorkingCopyScanner(reader, []).Scan();

        foreach (var entries in new[] { throughClient, throughWcDb })
        {
            await Assert.That(entries.Count(entry => entry.RelPath == "ext")).IsEqualTo(1);
            await Assert
                .That(
                    entries.Where(entry =>
                        entry.RelPath.StartsWith("ext/", StringComparison.Ordinal)
                    )
                )
                .IsEmpty();

            // Suppressing the contents is not enough on its own: an unversioned directory also
            // closes its subtree, so this would pass while telling the user to `svn add` an
            // external. The external's own `.svn` must not surface either.
            await Assert.That(Status(entries, "ext")).IsNotEqualTo(NodeStatus.Unversioned);
            await Assert
                .That(
                    entries.Where(entry => entry.RelPath.EndsWith(".svn", StringComparison.Ordinal))
                )
                .IsEmpty();
        }
    }

    /// <summary>
    /// The incremental path leans on an external having no NODES row: that silence is what makes
    /// wc.db refuse, instead of re-resolving another working copy's node as though it were ours.
    /// Asserted here because this is the only fixture that has an external to refuse.
    /// </summary>
    [Test]
    public async Task An_incremental_apply_refuses_a_change_at_an_external()
    {
        using var copy = WithObstructionAndExternal();
        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, []);
        var held = scanner.Scan();

        var applied = scanner.TryApplyChanges(
            held,
            new HashSet<string>(["ext"], StringComparer.Ordinal)
        );

        await Assert.That(applied).IsNull();
        // The negative case: an ordinary versioned file in the same tree is applied.
        await Assert
            .That(
                scanner.TryApplyChanges(
                    held,
                    new HashSet<string>(["untouched.txt"], StringComparer.Ordinal)
                )
            )
            .IsNotNull();
    }

    /// <summary>
    /// A versioned file with a directory now in its place, a versioned directory replaced by a
    /// file, and an <c>svn:externals</c> checkout. Its own fixture — see the test above for why.
    /// </summary>
    private static SvnWorkingCopy WithObstructionAndExternal()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("shared/thing.txt", "shared\n");
            copy.Write("file-now-dir", "a file for now\n");
            copy.Write("dir-now-file/child.txt", "child\n");
            copy.Write("untouched.txt", "untouched\n");
            copy.Svn("add", "--quiet", "shared", "file-now-dir", "dir-now-file", "untouched.txt");
            copy.Svn("commit", "--quiet", "-m", "first");
            copy.Svn("update", "--quiet");

            copy.Svn("propset", "--quiet", "svn:externals", "^/shared ext", ".");
            copy.Svn("commit", "--quiet", "-m", "externals");
            // The external is only checked out by an update, not by the commit that declares it.
            copy.Svn("update", "--quiet");

            copy.Delete("file-now-dir");
            copy.CreateDirectory("file-now-dir");
            Directory.Delete(copy.Absolute("dir-now-file"), recursive: true);
            copy.Write("dir-now-file", "a file now\n");

            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The two axes, the tree states, a lock, a changelist, an ignored file and an unversioned one —
    /// the shapes the wc.db path is tested against, so the comparison above has something to say.
    /// </summary>
    private static SvnWorkingCopy EveryShape()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("src/clean.txt", "clean\n");
            copy.Write("src/modified.txt", "before\n");
            copy.Write("src/deleted.txt", "doomed\n");
            copy.Write("src/missing.txt", "gone\n");
            copy.Write("src/locked.txt", "held\n");
            copy.Write("src/propmod.txt", "props\n");
            copy.Write("src/listed.txt", "listed\n");
            copy.Write("art/untouched.png", "untouched\n");
            copy.Write("art/edited.png", "edited\n");
            copy.Write("art/missing.png", "missing\n");
            copy.Write("art/deleted.png", "deleted\n");
            copy.Write("art/propmod.png", "propmod\n");
            copy.Write("art/replaced.png", "replaced\n");
            copy.Write("art/sub/deep.png", "deep\n");
            copy.Svn("add", "--quiet", "src", "art");
            copy.Svn("commit", "--quiet", "-m", "first");
            copy.Svn("update", "--quiet");

            copy.Write("src/modified.txt", "after\n");
            copy.Svn("delete", "--quiet", "src/deleted.txt");
            copy.Delete("src/missing.txt");
            // `svn lock` has no --quiet, unlike every other subcommand here.
            copy.Svn("lock", "src/locked.txt");
            copy.Svn("propset", "--quiet", "svn:mime-type", "text/plain", "src/propmod.txt");
            copy.Svn("changelist", "--quiet", "art-pass", "src/listed.txt");

            // Added and copied nodes carrying properties: the shape where svn's XML says
            // props="modified" and its own status column says nothing. Both readers must say
            // nothing too, and without these the comparison above never sees the difference.
            copy.Write("src/added.txt", "new\n");
            copy.Svn("add", "--quiet", "src/added.txt");
            copy.Svn("propset", "--quiet", "svn:mime-type", "text/plain", "src/added.txt");
            copy.Svn("copy", "--quiet", "src/clean.txt", "src/copied.txt");
            copy.Svn("propset", "--quiet", "svn:mime-type", "text/plain", "src/copied.txt");

            // A copied directory and one of every change inside it. Only a copied file was here
            // before, and every node inside a copied directory then read as added on one path
            // alone — this comparison would have said so, had it had one to look at.
            copy.Svn("copy", "--quiet", "art", "artcopy");
            copy.Write("artcopy/edited.png", "edited inside the copy\n");
            copy.Delete("artcopy/missing.png");
            copy.Svn("delete", "--quiet", "artcopy/deleted.png");
            copy.Svn("propset", "--quiet", "svn:mime-type", "image/png", "artcopy/propmod.png");
            copy.Svn("delete", "--quiet", "artcopy/replaced.png");
            copy.Svn("copy", "--quiet", "src/clean.txt", "artcopy/replaced.png");
            copy.Write("artcopy/fresh.png", "fresh\n");
            copy.Svn("add", "--quiet", "artcopy/fresh.png");

            copy.Svn("propset", "--quiet", "svn:ignore", "*.tmp", ".");
            copy.Write("build.tmp", "ignored\n");
            copy.Write("stray.txt", "unversioned\n");

            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Everything both paths claim to know, bar one: node kind, which <c>svn status</c> does not
    /// report at all. Revision is in here — it was excluded while the fast path read it off the
    /// highest op_depth row, and putting it back is the point of that fix.
    /// </summary>
    private static IEnumerable<string> Comparable(IEnumerable<WorkingCopyEntry> entries) =>
        entries
            .Select(entry =>
                $"{entry.RelPath}|{entry.Status}|{entry.PropertyStatus}|{entry.Revision}|"
                + $"{entry.Changelist}|{entry.IsConflicted}|{entry.HasLockToken}|{entry.IsCopied}"
            )
            .Order(StringComparer.Ordinal);

    private static NodeStatus Status(IEnumerable<WorkingCopyEntry> entries, string relPath) =>
        entries.Single(entry => entry.RelPath == relPath).Status;

    private static long? Revision(IEnumerable<WorkingCopyEntry> entries, string relPath) =>
        entries.Single(entry => entry.RelPath == relPath).Revision;
}
