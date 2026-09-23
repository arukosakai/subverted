using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// A copied directory against a real working copy, one node per shape <c>svn status</c> gave a
/// different answer for. Until these, every file inside a copy read as <c>A</c> — including one
/// deleted from disk, which a commit then publishes anyway, because a copy is made on the server.
/// </summary>
public sealed class CopyStatusIntegrationTests
{
    [Test]
    public async Task The_copy_root_is_an_add_with_history()
    {
        using var copy = CopiedDirectory();

        var root = EntryAt(Scan(copy), "copy");

        await Assert.That(root.Status).IsEqualTo(NodeStatus.Added);
        await Assert.That(root.IsCopied).IsTrue();
    }

    [Test]
    public async Task An_untouched_file_inside_the_copy_is_unmodified_with_history()
    {
        using var copy = CopiedDirectory();

        var untouched = EntryAt(Scan(copy), "copy/untouched.txt");

        await Assert.That(untouched.Status).IsEqualTo(NodeStatus.Unmodified);
        await Assert.That(untouched.IsCopied).IsTrue();
    }

    [Test]
    public async Task An_untouched_directory_inside_the_copy_is_unmodified_with_history()
    {
        using var copy = CopiedDirectory();

        var nested = EntryAt(Scan(copy), "copy/nested");

        await Assert.That(nested.Status).IsEqualTo(NodeStatus.Unmodified);
        await Assert.That(nested.IsCopied).IsTrue();
    }

    [Test]
    public async Task An_edit_inside_the_copy_is_a_modification_with_history()
    {
        using var copy = CopiedDirectory();
        copy.Write("copy/edited.txt", "edited inside the copy");

        var edited = EntryAt(Scan(copy), "copy/edited.txt");

        await Assert.That(edited.Status).IsEqualTo(NodeStatus.Modified);
        await Assert.That(edited.IsCopied).IsTrue();
    }

    /// <summary>
    /// The one that hid something. <c>svn status</c> prints <c>!</c> and drops the <c>+</c>; the
    /// scan called it added. Committed that way, the file goes to the server with the rest of the
    /// copy and the next update puts it back on disk — measured, and nothing along the way says so.
    /// </summary>
    [Test]
    public async Task A_file_deleted_from_disk_inside_the_copy_is_missing_without_history()
    {
        using var copy = CopiedDirectory();
        copy.Delete("copy/edited.txt");

        var gone = EntryAt(Scan(copy), "copy/edited.txt");

        await Assert.That(gone.Status).IsEqualTo(NodeStatus.Missing);
        await Assert.That(gone.IsCopied).IsFalse();
    }

    [Test]
    public async Task A_delete_inside_the_copy_carries_history()
    {
        using var copy = CopiedDirectory();
        copy.Svn("delete", "--quiet", "copy/edited.txt");

        var deleted = EntryAt(Scan(copy), "copy/edited.txt");

        await Assert.That(deleted.Status).IsEqualTo(NodeStatus.Deleted);
        await Assert.That(deleted.IsCopied).IsTrue();
    }

    [Test]
    public async Task A_property_set_inside_the_copy_is_a_property_change()
    {
        using var copy = CopiedDirectory();
        copy.Svn("propset", "--quiet", "studio:owner", "art", "copy/untouched.txt");

        var changed = EntryAt(Scan(copy), "copy/untouched.txt");

        await Assert.That(changed.Status).IsEqualTo(NodeStatus.Unmodified);
        await Assert.That(changed.PropertyStatus).IsEqualTo(PropertyStatus.Modified);
    }

    /// <summary>
    /// The negative side of the one above: at the copy's own root the add carries its properties
    /// and <c>svn status</c> leaves the column blank, as it always has.
    /// </summary>
    [Test]
    public async Task A_property_set_on_the_copy_root_stays_with_the_add()
    {
        using var copy = CopiedDirectory();
        copy.Svn("propset", "--quiet", "studio:owner", "art", "copy");

        await Assert
            .That(EntryAt(Scan(copy), "copy").PropertyStatus)
            .IsEqualTo(PropertyStatus.Unmodified);
    }

    /// <summary>A plain add inside a copy is its own operation, and it came from nowhere.</summary>
    [Test]
    public async Task A_plain_add_inside_the_copy_has_no_history()
    {
        using var copy = CopiedDirectory();
        copy.Write("copy/fresh.txt", "new");
        copy.Svn("add", "--quiet", "copy/fresh.txt");

        var fresh = EntryAt(Scan(copy), "copy/fresh.txt");

        await Assert.That(fresh.Status).IsEqualTo(NodeStatus.Added);
        await Assert.That(fresh.IsCopied).IsFalse();
    }

    /// <summary>
    /// A replace one layer above the copy rather than above BASE — there is no BASE row at all, so
    /// it read as an add. <c>svn status</c> prints <c>R  +</c>.
    /// </summary>
    [Test]
    public async Task A_replace_inside_the_copy_is_a_replace_with_history()
    {
        using var copy = CopiedDirectory();
        copy.Svn("delete", "--quiet", "copy/untouched.txt");
        copy.Svn("copy", "--quiet", "source/edited.txt", "copy/untouched.txt");

        var replaced = EntryAt(Scan(copy), "copy/untouched.txt");

        await Assert.That(replaced.Status).IsEqualTo(NodeStatus.Replaced);
        await Assert.That(replaced.IsCopied).IsTrue();
    }

    /// <summary>
    /// Both layers hold this child — BASE, and the copy that replaced its parent — and it read as
    /// <c>R</c> for having a BASE row. The replace is the parent's.
    /// </summary>
    [Test]
    public async Task A_child_of_a_directory_replaced_by_a_copy_is_not_itself_replaced()
    {
        using var copy = Committed(("target/same.txt", "base"), ("source/same.txt", "copied"));
        copy.Svn("delete", "--quiet", "target");
        copy.Svn("copy", "--quiet", "source", "target");

        var entries = Scan(copy);

        await Assert.That(EntryAt(entries, "target").Status).IsEqualTo(NodeStatus.Replaced);
        await Assert
            .That(EntryAt(entries, "target/same.txt").Status)
            .IsEqualTo(NodeStatus.Unmodified);
        await Assert.That(EntryAt(entries, "target/same.txt").IsCopied).IsTrue();
    }

    [Test]
    public async Task Nothing_committed_or_plainly_added_claims_history()
    {
        using var copy = CopiedDirectory();
        copy.Write("plain.txt", "new");
        copy.Svn("add", "--quiet", "plain.txt");

        var entries = Scan(copy);

        await Assert.That(EntryAt(entries, "source/untouched.txt").IsCopied).IsFalse();
        await Assert.That(EntryAt(entries, "plain.txt").IsCopied).IsFalse();
    }

    /// <summary>
    /// The incremental path re-reads one row; it has to land on the rescan's answer here too, or
    /// the first save inside a copy would put the <c>A</c> back.
    /// </summary>
    [Test]
    public async Task An_edit_inside_the_copy_applied_incrementally_matches_a_rescan()
    {
        using var copy = CopiedDirectory();
        var held = Scan(copy);
        copy.Write("copy/edited.txt", "edited inside the copy");

        using var reader = WcDbReader.Open(copy.Root);
        var applied = new WorkingCopyScanner(
            reader,
            GlobalIgnoreConfiguration.SubversionDefault
        ).TryApplyChanges(held, new HashSet<string>(["copy/edited.txt"], StringComparer.Ordinal));

        await Assert.That(applied).IsNotNull();
        await Assert
            .That(EntryAt(applied!, "copy/edited.txt"))
            .IsEqualTo(EntryAt(Scan(copy), "copy/edited.txt"));
        await Assert.That(EntryAt(applied!, "copy/edited.txt").IsCopied).IsTrue();
    }

    private static SvnWorkingCopy CopiedDirectory()
    {
        var copy = Committed(
            ("source/untouched.txt", "untouched"),
            ("source/edited.txt", "edited"),
            ("source/nested/deep.txt", "deep")
        );
        copy.Svn("copy", "--quiet", "source", "copy");
        return copy;
    }

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

    private static IReadOnlyList<WorkingCopyEntry> Scan(SvnWorkingCopy copy)
    {
        using var reader = WcDbReader.Open(copy.Root);
        return new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault).Scan();
    }

    private static WorkingCopyEntry EntryAt(
        IReadOnlyList<WorkingCopyEntry> entries,
        string relPath
    ) => entries.Single(entry => entry.RelPath == relPath);
}
