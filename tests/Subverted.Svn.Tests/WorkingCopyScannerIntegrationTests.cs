using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// End-to-end against a real checkout. These are the claims PLAN.md makes about M0, turned into
/// something that fails when they stop being true.
/// </summary>
public sealed class WorkingCopyScannerIntegrationTests
{
    [Test]
    public async Task A_fresh_checkout_reports_every_node_unmodified()
    {
        using var copy = Committed(("assets/hero.png", "pixels"), ("readme.txt", "hello"));

        var entries = Scan(copy);

        await Assert.That(StatusOf(entries, "assets/hero.png")).IsEqualTo(NodeStatus.Unmodified);
        await Assert.That(StatusOf(entries, "assets")).IsEqualTo(NodeStatus.Unmodified);
        await Assert.That(StatusOf(entries, "readme.txt")).IsEqualTo(NodeStatus.Unmodified);
        await Assert.That(StatusOf(entries, string.Empty)).IsEqualTo(NodeStatus.Unmodified);
    }

    [Test]
    public async Task Changed_content_is_modified()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("readme.txt", "hello, world");

        await Assert.That(StatusOf(Scan(copy), "readme.txt")).IsEqualTo(NodeStatus.Modified);
    }

    /// <summary>
    /// The case the checksum fast path exists for: saving a file without editing it moves the
    /// mtime, and calling that modified is the bug that would make the tool cry wolf all day.
    /// </summary>
    [Test]
    public async Task A_rewrite_with_identical_bytes_is_unmodified()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("readme.txt", "hello");
        File.SetLastWriteTimeUtc(
            copy.Absolute("readme.txt"),
            new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );

        await Assert.That(StatusOf(Scan(copy), "readme.txt")).IsEqualTo(NodeStatus.Unmodified);
    }

    /// <summary>A same-length change defeats the size check, so only a content compare catches it.</summary>
    [Test]
    public async Task A_rewrite_of_the_same_length_with_different_bytes_is_modified()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("readme.txt", "world");
        File.SetLastWriteTimeUtc(
            copy.Absolute("readme.txt"),
            new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );

        await Assert.That(StatusOf(Scan(copy), "readme.txt")).IsEqualTo(NodeStatus.Modified);
    }

    [Test]
    public async Task A_file_deleted_behind_svns_back_is_missing()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Delete("readme.txt");

        await Assert.That(StatusOf(Scan(copy), "readme.txt")).IsEqualTo(NodeStatus.Missing);
    }

    [Test]
    public async Task A_scheduled_add_is_added()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("fresh.txt", "new");
        copy.Svn("add", "--quiet", "fresh.txt");

        await Assert.That(StatusOf(Scan(copy), "fresh.txt")).IsEqualTo(NodeStatus.Added);
    }

    [Test]
    public async Task A_scheduled_delete_is_deleted()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Svn("delete", "--quiet", "readme.txt");

        await Assert.That(StatusOf(Scan(copy), "readme.txt")).IsEqualTo(NodeStatus.Deleted);
    }

    [Test]
    public async Task A_changelist_is_carried_through()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Svn("changelist", "review", "readme.txt", "--quiet");

        var entry = Scan(copy).Single(e => e.RelPath == "readme.txt");

        await Assert.That(entry.Changelist).IsEqualTo("review");
    }

    [Test]
    public async Task An_untracked_file_is_unversioned()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("scratch.txt", "not mine");

        await Assert.That(StatusOf(Scan(copy), "scratch.txt")).IsEqualTo(NodeStatus.Unversioned);
    }

    [Test]
    public async Task A_file_matching_svn_ignore_is_ignored_and_its_neighbour_is_not()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Svn("propset", "svn:ignore", "*.tmp", ".");
        copy.Write("scratch.tmp", "junk");
        copy.Write("scratch.txt", "junk");

        var entries = Scan(copy);

        await Assert.That(StatusOf(entries, "scratch.tmp")).IsEqualTo(NodeStatus.Ignored);
        await Assert.That(StatusOf(entries, "scratch.txt")).IsEqualTo(NodeStatus.Unversioned);
    }

    /// <summary>
    /// `svn:ignore` reaches immediate children only — a nested file of the same name stays
    /// reported, which is the rule a recursive implementation would silently break.
    /// </summary>
    [Test]
    public async Task Svn_ignore_does_not_reach_into_a_subdirectory()
    {
        using var copy = Committed(("sub/keep.txt", "hello"));
        copy.Svn("propset", "svn:ignore", "*.tmp", ".");
        copy.Write("root.tmp", "junk");
        copy.Write("sub/nested.tmp", "junk");

        var entries = Scan(copy);

        await Assert.That(StatusOf(entries, "root.tmp")).IsEqualTo(NodeStatus.Ignored);
        await Assert.That(StatusOf(entries, "sub/nested.tmp")).IsEqualTo(NodeStatus.Unversioned);
    }

    /// <summary>`svn:global-ignores` is the one that does reach down.</summary>
    [Test]
    public async Task Svn_global_ignores_does_reach_into_a_subdirectory()
    {
        using var copy = Committed(("sub/keep.txt", "hello"));
        copy.Svn("propset", "svn:global-ignores", "*.tmp", ".");
        copy.Write("sub/nested.tmp", "junk");

        await Assert.That(StatusOf(Scan(copy), "sub/nested.tmp")).IsEqualTo(NodeStatus.Ignored);
    }

    /// <summary>
    /// SVN reports an unversioned directory once and never lists what is inside it. Listing the
    /// contents would bury a real answer under a build output tree.
    /// </summary>
    [Test]
    public async Task An_unversioned_directory_is_reported_without_its_contents()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("junk/inner.txt", "x");
        copy.Write("junk/deeper/inner.txt", "x");

        var entries = Scan(copy);

        await Assert.That(StatusOf(entries, "junk")).IsEqualTo(NodeStatus.Unversioned);
        await Assert.That(entries.Any(e => e.RelPath.StartsWith("junk/"))).IsFalse();
    }

    /// <summary>
    /// A sparse checkout leaves a row wc.db tracks but never materialises. Reporting it would show
    /// the user a node that is not there; nothing is on disk at that path either, so it must not
    /// come back as unversioned through the other door.
    /// </summary>
    [Test]
    public async Task An_excluded_node_is_reported_neither_as_versioned_nor_as_unversioned()
    {
        using var copy = Committed(("sub/keep.txt", "hello"), ("readme.txt", "hello"));
        copy.Svn("update", "--quiet", "--set-depth", "exclude", "sub");

        var entries = Scan(copy);

        await Assert.That(entries.Any(e => e.RelPath.StartsWith("sub"))).IsFalse();
        await Assert.That(StatusOf(entries, "readme.txt")).IsEqualTo(NodeStatus.Unmodified);
    }

    [Test]
    public async Task A_property_only_change_leaves_content_clean_and_marks_properties()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Svn("propset", "svn:mime-type", "text/plain", "readme.txt");

        var entry = Scan(copy).Single(e => e.RelPath == "readme.txt");

        await Assert.That(entry.Status).IsEqualTo(NodeStatus.Unmodified);
        await Assert.That(entry.PropertyStatus).IsEqualTo(PropertyStatus.Modified);
    }

    /// <summary>
    /// The regression this caught: a propset that invalidates SVN's size cache left the node
    /// reporting Modified, because -1 was read as a real size and never matched the file's length.
    /// `svn status` calls all three of these text-clean.
    /// </summary>
    [Test]
    [Arguments("svn:mime-type", "text/plain")]
    [Arguments("svn:needs-lock", "yes")]
    [Arguments("svn:eol-style", "native")]
    public async Task Setting_a_property_never_makes_the_content_look_modified(
        string property,
        string value
    )
    {
        using var copy = Committed(("readme.txt", "hello\n"));
        copy.Svn("propset", property, value, "readme.txt");

        await Assert.That(StatusOf(Scan(copy), "readme.txt")).IsNotEqualTo(NodeStatus.Modified);
    }

    [Test]
    public async Task An_untouched_node_is_clean_on_both_axes()
    {
        using var copy = Committed(("readme.txt", "hello"));

        var entry = Scan(copy).Single(e => e.RelPath == "readme.txt");

        await Assert.That(entry.Status).IsEqualTo(NodeStatus.Unmodified);
        await Assert.That(entry.PropertyStatus).IsEqualTo(PropertyStatus.Unmodified);
    }

    /// <summary>
    /// Setting <c>svn:eol-style</c> blanks SVN's recorded size and mtime, so both of these reach
    /// the content compare. From there the only difference is translation: the untranslated file
    /// is settled by hashing it, the translated one cannot be — its working bytes are not its
    /// pristine bytes — so it escalates instead of guessing. `svn status` calls both text-clean;
    /// resolving the second the way SVN does needs its translation, which is the documented fall
    /// back to the CLI.
    /// </summary>
    [Test]
    [Arguments(false, NodeStatus.Unmodified)]
    [Arguments(true, NodeStatus.NeedsPristineCompare)]
    public async Task Only_a_translated_file_escalates_when_its_mtime_moves(
        bool translated,
        NodeStatus expected
    )
    {
        using var copy = Committed(("readme.txt", "hello\n"));
        if (translated)
        {
            copy.Svn("propset", "svn:eol-style", "native", "readme.txt");
        }

        File.SetLastWriteTimeUtc(
            copy.Absolute("readme.txt"),
            new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );

        await Assert.That(StatusOf(Scan(copy), "readme.txt")).IsEqualTo(expected);
    }

    /// <summary>
    /// Every rewritten node here lands on the content compare, which the scanner runs across
    /// threads. The bytes are all the same length and the clean and modified ones alternate, so a
    /// verdict delivered to the wrong node shows up as a wrong status rather than as a tally that
    /// still adds up. <c>keep.txt</c> never reaches the compare at all and pins that settling the
    /// batch leaves its neighbours alone.
    /// </summary>
    [Test]
    public async Task Each_node_settled_by_content_keeps_its_own_verdict()
    {
        const int Count = 40;
        var names = Enumerable.Range(0, Count).Select(i => $"n{i:D2}.txt").ToArray();

        using var copy = Committed([
            .. names.Select(name => (name, "abcde")),
            ("keep.txt", "untouched"),
        ]);

        foreach (var (name, index) in names.Select((name, index) => (name, index)))
        {
            copy.Write(name, index % 2 == 0 ? "abcde" : "vwxyz");
            File.SetLastWriteTimeUtc(
                copy.Absolute(name),
                new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            );
        }

        var entries = Scan(copy);

        foreach (var (name, index) in names.Select((name, index) => (name, index)))
        {
            await Assert
                .That(StatusOf(entries, name))
                .IsEqualTo(index % 2 == 0 ? NodeStatus.Unmodified : NodeStatus.Modified);
        }

        await Assert.That(StatusOf(entries, "keep.txt")).IsEqualTo(NodeStatus.Unmodified);
    }

    /// <summary>
    /// The claim the whole incremental path rests on: re-resolving the one path that moved has to
    /// land on the answer a full rescan would have given. Asserted against a real rescan of the
    /// same tree rather than against an expected list, so it cannot drift as the rules change.
    /// </summary>
    [Test]
    public async Task Applying_one_change_lands_where_a_full_rescan_would()
    {
        using var copy = Committed(
            ("readme.txt", "hello"),
            ("sub/keep.txt", "keep"),
            ("sub/other.txt", "other")
        );
        var held = Scan(copy);
        copy.Write("sub/keep.txt", "edited");

        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);
        var applied = scanner.TryApplyChanges(held, Paths("sub/keep.txt"));

        await Assert.That(applied).IsNotNull();
        await Assert.That(Comparable(applied!)).IsEquivalentTo(Comparable(Scan(copy)));
        await Assert.That(StatusOf(applied!, "sub/keep.txt")).IsEqualTo(NodeStatus.Modified);
        // The rest of the list is carried over untouched, not re-derived and not dropped.
        await Assert.That(StatusOf(applied!, "sub/other.txt")).IsEqualTo(NodeStatus.Unmodified);
        await Assert.That(applied!.Count).IsEqualTo(held.Count);
    }

    [Test]
    public async Task A_versioned_file_deleted_from_disk_is_applied_as_missing()
    {
        using var copy = Committed(("readme.txt", "hello"), ("gone.txt", "bye"));
        var held = Scan(copy);
        copy.Delete("gone.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);
        var applied = scanner.TryApplyChanges(held, Paths("gone.txt"));

        await Assert.That(applied).IsNotNull();
        await Assert.That(StatusOf(applied!, "gone.txt")).IsEqualTo(NodeStatus.Missing);
        await Assert.That(Comparable(applied!)).IsEquivalentTo(Comparable(Scan(copy)));
    }

    /// <summary>
    /// A file the last scan never saw changes the *set* of nodes, and the set is decided by
    /// ignore-rule inheritance and unversioned-subtree closing — whole-tree properties. Refusing
    /// is what keeps a new file from being listed under rules that were never evaluated for it.
    /// </summary>
    [Test]
    public async Task A_path_the_last_scan_never_saw_refuses_and_leaves_it_to_a_rescan()
    {
        using var copy = Committed(("readme.txt", "hello"));
        var held = Scan(copy);
        copy.Write("brand-new.txt", "new");

        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);

        await Assert.That(scanner.TryApplyChanges(held, Paths("brand-new.txt"))).IsNull();
    }

    /// <summary>
    /// An unversioned, ignored or external path has no NODES row, so wc.db is what refuses — the
    /// single-node read comes back empty. A guard on the held status would be a second check that
    /// could never fire, which is why there is not one.
    /// </summary>
    [Test]
    public async Task A_path_the_last_scan_called_unversioned_refuses()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("stray.txt", "stray");
        var held = Scan(copy);

        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);

        await Assert.That(StatusOf(held, "stray.txt")).IsEqualTo(NodeStatus.Unversioned);
        await Assert.That(scanner.TryApplyChanges(held, Paths("stray.txt"))).IsNull();
    }

    /// <summary>
    /// Anything under the admin directory means SVN itself wrote — an add, a commit, an update —
    /// which can move nodes this change set says nothing about. It refuses for free, because
    /// <c>.svn</c> is never a node the scan holds.
    /// </summary>
    [Test]
    public async Task A_change_inside_the_admin_directory_refuses()
    {
        using var copy = Committed(("readme.txt", "hello"));
        var held = Scan(copy);

        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);

        await Assert.That(scanner.TryApplyChanges(held, Paths(".svn/wc.db"))).IsNull();
    }

    /// <summary>
    /// One unknown path poisons the batch: applying the rest and ignoring it would drop a change
    /// on the floor, which is the one outcome worse than a slow answer.
    /// </summary>
    [Test]
    public async Task One_unknown_path_refuses_the_whole_batch()
    {
        using var copy = Committed(("readme.txt", "hello"));
        var held = Scan(copy);
        copy.Write("readme.txt", "edited");

        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);

        await Assert
            .That(scanner.TryApplyChanges(held, Paths("readme.txt", "brand-new.txt")))
            .IsNull();
    }

    [Test]
    public async Task Applying_nothing_hands_back_exactly_what_was_held()
    {
        using var copy = Committed(("readme.txt", "hello"));
        var held = Scan(copy);

        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);

        await Assert.That(scanner.TryApplyChanges(held, Paths())).IsSameReferenceAs(held);
    }

    /// <summary>
    /// A property change is an <c>ACTUAL_NODE</c> write, so it moves the second axis without
    /// touching the file. The re-resolved entry has to carry it, or an incremental update reports
    /// a node clean on a column a full rescan flags.
    /// </summary>
    [Test]
    public async Task An_applied_entry_carries_the_property_axis_and_the_revision()
    {
        using var copy = Committed(("readme.txt", "hello"));
        var held = Scan(copy);
        copy.Svn("propset", "--quiet", "svn:mime-type", "text/plain", "readme.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);
        var applied = scanner.TryApplyChanges(held, Paths("readme.txt"));

        await Assert.That(applied).IsNotNull();
        var entry = applied!.Single(e => e.RelPath == "readme.txt");
        await Assert.That(entry.PropertyStatus).IsEqualTo(PropertyStatus.Modified);
        await Assert.That(entry.Revision).IsEqualTo(1);
        await Assert.That(Comparable(applied!)).IsEquivalentTo(Comparable(Scan(copy)));
    }

    [Test]
    public async Task A_versioned_file_carries_its_size_and_write_time()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("readme.txt", "hello, world");
        File.SetLastWriteTimeUtc(copy.Absolute("readme.txt"), SomeInstant);

        await Assert
            .That(OnDiskOf(Scan(copy), "readme.txt"))
            .IsEqualTo(new FileFingerprint(12, SomeInstant));
    }

    /// <summary>
    /// The node the metadata fast path hands to the content compare is rebuilt with its verdict, and
    /// rebuilding it must not drop what the walk already knew about the file.
    /// </summary>
    [Test]
    public async Task A_file_settled_by_content_keeps_its_fingerprint()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("readme.txt", "world");
        File.SetLastWriteTimeUtc(copy.Absolute("readme.txt"), SomeInstant);

        var entry = Scan(copy).Single(e => e.RelPath == "readme.txt");

        await Assert.That(entry.Status).IsEqualTo(NodeStatus.Modified);
        await Assert.That(entry.OnDisk).IsEqualTo(new FileFingerprint(5, SomeInstant));
    }

    [Test]
    public async Task An_unversioned_file_carries_its_fingerprint()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("scratch.txt", "junk");
        File.SetLastWriteTimeUtc(copy.Absolute("scratch.txt"), SomeInstant);

        await Assert
            .That(OnDiskOf(Scan(copy), "scratch.txt"))
            .IsEqualTo(new FileFingerprint(4, SomeInstant));
    }

    [Test]
    public async Task A_directory_and_a_missing_file_have_no_fingerprint()
    {
        using var copy = Committed(("assets/hero.png", "pixels"), ("gone.txt", "bye"));
        copy.Delete("gone.txt");

        var entries = Scan(copy);

        await Assert.That(OnDiskOf(entries, "assets")).IsNull();
        await Assert.That(OnDiskOf(entries, "gone.txt")).IsNull();
        await Assert.That(OnDiskOf(entries, "assets/hero.png")).IsNotNull();
    }

    /// <summary>
    /// The case the fingerprint exists for: a file already <c>M</c> edited again reads as the same
    /// status, so without this the incremental path hands back an entry nothing can tell from the old.
    /// </summary>
    [Test]
    public async Task Re_editing_a_modified_file_is_applied_as_a_new_fingerprint()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("readme.txt", "first edit");
        File.SetLastWriteTimeUtc(copy.Absolute("readme.txt"), SomeInstant);
        var held = Scan(copy);
        copy.Write("readme.txt", "second edit");
        File.SetLastWriteTimeUtc(copy.Absolute("readme.txt"), SomeInstant.AddTicks(1));

        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);
        var applied = scanner.TryApplyChanges(held, Paths("readme.txt"));

        await Assert.That(StatusOf(applied!, "readme.txt")).IsEqualTo(NodeStatus.Modified);
        await Assert
            .That(OnDiskOf(applied!, "readme.txt"))
            .IsEqualTo(new FileFingerprint(11, SomeInstant.AddTicks(1)));
        await Assert.That(Comparable(applied!)).IsEquivalentTo(Comparable(Scan(copy)));
    }

    private static readonly DateTime SomeInstant = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static FileFingerprint? OnDiskOf(
        IReadOnlyList<WorkingCopyEntry> entries,
        string relPath
    ) => entries.Single(entry => entry.RelPath == relPath).OnDisk;

    private static IReadOnlySet<string> Paths(params string[] relPaths) =>
        new HashSet<string>(relPaths, StringComparer.Ordinal);

    /// <summary>Everything an entry claims, so an equality check cannot pass on the paths alone.</summary>
    private static IEnumerable<string> Comparable(IEnumerable<WorkingCopyEntry> entries) =>
        entries
            .Select(entry =>
                $"{entry.RelPath}|{entry.Kind}|{entry.Status}|{entry.PropertyStatus}|"
                + $"{entry.Revision}|{entry.Changelist}|{entry.IsConflicted}|{entry.HasLockToken}|"
                + $"{entry.OnDisk?.Length}|{entry.OnDisk?.LastWriteTimeUtc.Ticks}"
            )
            .Order(StringComparer.Ordinal);

    private static SvnWorkingCopy Committed(params (string RelPath, string Content)[] files)
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            foreach (var (relPath, content) in files)
            {
                copy.Write(relPath, content);
            }

            copy.Svn("add", "--quiet", "--force", ".");
            copy.Svn("commit", "--quiet", "-m", "fixture");
            copy.Svn("update", "--quiet");
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The third column, which the scan reported as blank until D25 — so a working copy every write
    /// was about to fail on read back as perfectly clean. The lock is on the root at every level,
    /// which is the row a client killed mid-commit really leaves.
    /// </summary>
    [Test]
    public async Task A_write_locked_working_copy_reports_the_lock_on_every_directory_it_reaches()
    {
        using var copy = Committed(("assets/hero.png", "pixels"), ("readme.txt", "hello"));
        WorkingCopyWedge.TakeWriteLock(copy, string.Empty, -1);

        var entries = Scan(copy);

        await Assert.That(WriteLockOn(entries, string.Empty)).IsTrue();
        await Assert.That(WriteLockOn(entries, "assets")).IsTrue();
    }

    /// <summary>
    /// Files are never write-locked — the column wc.db keeps is <c>local_dir_relpath</c> — and
    /// <c>svn status</c> prints <c>L</c> against directories only.
    /// </summary>
    [Test]
    public async Task A_file_under_a_write_locked_directory_is_not_itself_locked()
    {
        using var copy = Committed(("assets/hero.png", "pixels"));
        WorkingCopyWedge.TakeWriteLock(copy, string.Empty, -1);

        await Assert.That(WriteLockOn(Scan(copy), "assets/hero.png")).IsFalse();
    }

    /// <summary>
    /// The negative side: the lock reaches the directory it names and no further, so a sibling is
    /// left alone. Without this, "every directory is locked" would pass the test above.
    /// </summary>
    [Test]
    public async Task A_lock_on_one_directory_leaves_its_siblings_and_the_root_alone()
    {
        using var copy = Committed(("assets/hero.png", "pixels"), ("src/a.txt", "one"));
        WorkingCopyWedge.TakeWriteLock(copy, "assets", 0);

        var entries = Scan(copy);

        await Assert.That(WriteLockOn(entries, "assets")).IsTrue();
        await Assert.That(WriteLockOn(entries, "src")).IsFalse();
        await Assert.That(WriteLockOn(entries, string.Empty)).IsFalse();
    }

    [Test]
    public async Task A_healthy_working_copy_reports_no_write_lock_anywhere()
    {
        using var copy = Committed(("assets/hero.png", "pixels"));

        await Assert.That(Scan(copy).Any(entry => entry.IsWriteLocked)).IsFalse();
    }

    [Test]
    public async Task A_healthy_working_copy_has_no_interrupted_operation()
    {
        using var copy = Committed(("assets/hero.png", "pixels"));

        await Assert.That(UnfinishedOperations(copy)).IsEqualTo(0);
    }

    /// <summary>
    /// The state <c>svn status</c> answers <c>E155037</c> to and reads nothing in. The scanner
    /// reads it anyway, which is why it has to be able to say so.
    /// </summary>
    [Test]
    public async Task A_queued_step_of_an_interrupted_operation_is_counted()
    {
        using var copy = Committed(("assets/hero.png", "pixels"));
        WorkingCopyWedge.QueueInterruptedWork(copy, "assets/hero.png");

        await Assert.That(UnfinishedOperations(copy)).IsEqualTo(1);
    }

    /// <summary>
    /// A write lock is the other wedged state, and it does not stop <c>svn status</c> — counting
    /// the two together would report a copy that reads fine as one that does not.
    /// </summary>
    [Test]
    public async Task A_write_lock_is_not_an_interrupted_operation()
    {
        using var copy = Committed(("assets/hero.png", "pixels"));
        WorkingCopyWedge.TakeWriteLock(copy, string.Empty, -1);

        await Assert.That(UnfinishedOperations(copy)).IsEqualTo(0);
    }

    private static IReadOnlyList<WorkingCopyEntry> Scan(SvnWorkingCopy copy)
    {
        using var reader = WcDbReader.Open(copy.Root);
        return new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault).Scan();
    }

    private static int UnfinishedOperations(SvnWorkingCopy copy)
    {
        using var reader = WcDbReader.Open(copy.Root);
        return new WorkingCopyScanner(
            reader,
            GlobalIgnoreConfiguration.SubversionDefault
        ).CountUnfinishedOperations();
    }

    private static NodeStatus StatusOf(IReadOnlyList<WorkingCopyEntry> entries, string relPath) =>
        entries.Single(entry => entry.RelPath == relPath).Status;

    private static bool WriteLockOn(IReadOnlyList<WorkingCopyEntry> entries, string relPath) =>
        entries.Single(entry => entry.RelPath == relPath).IsWriteLocked;
}
