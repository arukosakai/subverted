using Microsoft.Data.Sqlite;
using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// Covers the parts of the reader that only a real wc.db can exercise: locating it, gating on its
/// format, and the SQL that projects the NODES table.
/// </summary>
public sealed class WcDbReaderIntegrationTests
{
    [Test]
    public async Task Opening_from_a_nested_directory_finds_the_working_copy_root()
    {
        using var copy = Committed(("assets/characters/hero.png", "pixels"));

        using var reader = WcDbReader.Open(copy.Absolute("assets/characters"));

        await Assert
            .That(Path.GetFullPath(reader.Info.RootPath))
            .IsEqualTo(Path.GetFullPath(copy.Root));
    }

    [Test]
    public async Task Opening_from_a_file_path_finds_the_working_copy_root()
    {
        using var copy = Committed(("readme.txt", "hello"));

        using var reader = WcDbReader.Open(copy.Absolute("readme.txt"));

        await Assert
            .That(Path.GetFullPath(reader.Info.RootPath))
            .IsEqualTo(Path.GetFullPath(copy.Root));
    }

    [Test]
    public async Task Opening_somewhere_with_no_working_copy_says_so()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"subverted-none-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            var thrown = await Assert.That(() => WcDbReader.Open(outside)).Throws<WcDbException>();

            await Assert.That(thrown!.Failure).IsEqualTo(WcDbFailure.NotAWorkingCopy);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    /// <summary>
    /// The version gate is what makes reading a private schema defensible: on a format we have not
    /// checked, the only safe answer is to refuse and let the caller shell out to <c>svn</c>.
    /// </summary>
    [Test]
    public async Task An_unsupported_format_is_refused_rather_than_guessed_at()
    {
        using var copy = Committed(("readme.txt", "hello"));
        SetFormat(copy, 99);

        var thrown = await Assert
            .That(() => WcDbReader.Open(copy.Root))
            .Throws<WcDbException>()
            .WithMessageContaining("99");

        // A working copy that is really there, read by a build that does not know its schema, is
        // the case the CLI fallback exists for — not the same answer as "wrong path".
        await Assert.That(thrown!.Failure).IsEqualTo(WcDbFailure.Unreadable);
    }

    [Test]
    public async Task A_supported_format_is_accepted()
    {
        using var copy = Committed(("readme.txt", "hello"));
        SetFormat(copy, 29);

        using var reader = WcDbReader.Open(copy.Root);

        await Assert.That(reader.Info.Format).IsEqualTo(29);
    }

    [Test]
    public async Task The_repository_root_and_uuid_are_read()
    {
        using var copy = Committed(("readme.txt", "hello"));

        using var reader = WcDbReader.Open(copy.Root);

        await Assert.That(reader.Info.RepositoryRoot).StartsWith("file:///");
        await Assert.That(Guid.TryParse(reader.Info.RepositoryUuid, out _)).IsTrue();
        // Not IsBetween: Format is nullable now, so "the client wrote a format at all" is half of
        // what this asserts — the CLI fallback is the path that has none.
        await Assert.That(reader.Info.Format is >= 29 and <= 31).IsTrue();
    }

    /// <summary>
    /// The root carries the working copy's own properties and <c>svn status</c> reports it as
    /// <c>.</c>, so leaving it out of the projection silently drops a node the user can commit.
    /// </summary>
    [Test]
    public async Task The_working_copy_root_is_one_of_the_nodes()
    {
        using var copy = Committed(("readme.txt", "hello"));

        using var reader = WcDbReader.Open(copy.Root);
        var root = reader.ReadNodes().Single(row => row.RelPath.Length == 0);

        await Assert.That(root.Kind).IsEqualTo(NodeKind.Directory);
        await Assert.That(root.OpDepth).IsEqualTo(0);
        await Assert.That(root.Presence).IsEqualTo("normal");
    }

    [Test]
    public async Task A_committed_file_carries_its_revision_and_recorded_size()
    {
        using var copy = Committed(("readme.txt", "hello"));

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNodes().Single(r => r.RelPath == "readme.txt");

        await Assert.That(row.BaseRevision).IsEqualTo(1);
        await Assert.That(row.RecordedSize).IsEqualTo(5);
        await Assert.That(row.LowerOpDepth).IsNull();
        await Assert.That(row.Checksum).IsNotNull().And.StartsWith("$sha1$");
    }

    /// <summary>
    /// The revision is the one column that must come from BASE rather than from the projected row.
    /// At op_depth &gt; 0 the NODES revision column means something else entirely — the copyfrom
    /// revision for a copy, nothing at all for a delete — so reading it disagreed with
    /// <c>svn status -v</c> in opposite directions on the two shapes.
    /// </summary>
    [Test]
    public async Task A_locally_deleted_node_keeps_the_revision_it_is_being_deleted_from()
    {
        using var copy = Committed(("doomed.txt", "bye"));
        copy.Svn("delete", "--quiet", "doomed.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNodes().Single(r => r.RelPath == "doomed.txt");

        await Assert.That(row.OpDepth).IsEqualTo(1);
        await Assert.That(row.BaseRevision).IsEqualTo(1);
    }

    /// <summary>
    /// A replace is a delete and an add at one path: the local layer carries no revision, but BASE
    /// still does, and <c>svn status -v</c> prints it.
    /// </summary>
    [Test]
    public async Task A_replaced_node_keeps_the_base_revision_under_it()
    {
        using var copy = Committed(("swapped.txt", "old"));
        copy.Svn("delete", "--quiet", "swapped.txt");
        copy.Write("swapped.txt", "new");
        copy.Svn("add", "--quiet", "swapped.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNodes().Single(r => r.RelPath == "swapped.txt");

        await Assert.That(row.OpDepth).IsEqualTo(1);
        await Assert.That(row.BaseRevision).IsEqualTo(1);
    }

    /// <summary>
    /// The other direction, and the one a naive "read the projected row" gets wrong: the copy's
    /// NODES row carries its *source's* revision, and reporting that claims the node exists on the
    /// server at a revision where it does not.
    /// </summary>
    [Test]
    public async Task A_copy_reports_no_revision_rather_than_the_revision_it_was_copied_from()
    {
        using var copy = Committed(("original.txt", "source"));
        copy.Svn("copy", "--quiet", "original.txt", "duplicate.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var rows = reader.ReadNodes().ToList();

        await Assert.That(rows.Single(r => r.RelPath == "duplicate.txt").BaseRevision).IsNull();
        await Assert.That(rows.Single(r => r.RelPath == "original.txt").BaseRevision).IsEqualTo(1);
    }

    /// <summary>
    /// A copy's row names the repository node it came from, and a plain add's names nothing; that
    /// column is the whole difference between <c>A  +</c> and <c>A</c>.
    /// </summary>
    [Test]
    public async Task A_copy_names_a_source_and_has_nothing_beneath_it()
    {
        using var copy = Committed(("original.txt", "source"));
        copy.Svn("copy", "--quiet", "original.txt", "duplicate.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNodes().Single(r => r.RelPath == "duplicate.txt");

        await Assert.That(row.HasCopySource).IsTrue();
        await Assert.That(row.LowerOpDepth).IsNull();
    }

    [Test]
    public async Task A_replace_sees_the_base_layer_beneath_it()
    {
        using var copy = Committed(("swapped.txt", "old"), ("donor.txt", "new"));
        copy.Svn("delete", "--quiet", "swapped.txt");
        copy.Svn("copy", "--quiet", "donor.txt", "swapped.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNodes().Single(r => r.RelPath == "swapped.txt");

        await Assert.That(row.OpDepth).IsEqualTo(1);
        await Assert.That(row.LowerOpDepth).IsEqualTo(0);
    }

    /// <summary>
    /// The layer beneath is the next one down, not BASE: a replace inside a copied directory sits
    /// over the copy at op_depth 1 and has no BASE row at all.
    /// </summary>
    [Test]
    public async Task A_replace_inside_a_copy_sees_the_copy_beneath_it_rather_than_base()
    {
        using var copy = Committed(("dir/child.txt", "child"), ("donor.txt", "new"));
        copy.Svn("copy", "--quiet", "dir", "dircopy");
        copy.Svn("delete", "--quiet", "dircopy/child.txt");
        copy.Svn("copy", "--quiet", "donor.txt", "dircopy/child.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNodes().Single(r => r.RelPath == "dircopy/child.txt");

        await Assert.That(row.OpDepth).IsEqualTo(2);
        await Assert.That(row.LowerOpDepth).IsEqualTo(1);
        await Assert.That(row.BaseRevision).IsNull();
    }

    [Test]
    public async Task A_single_node_read_sees_the_same_layers_as_the_whole_tree()
    {
        using var copy = Committed(("dir/child.txt", "child"));
        copy.Svn("copy", "--quiet", "dir", "dircopy");

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNode("dircopy/child.txt");

        await Assert
            .That(row)
            .IsEqualTo(reader.ReadNodes().Single(r => r.RelPath == "dircopy/child.txt"));
        await Assert.That(row!.IsWithinCopy).IsTrue();
    }

    /// <summary>
    /// A plain add has no BASE row at all, so it has no revision for a different reason than a copy
    /// does. Both answer null, and a fix that only special-cased copies would still be wrong here.
    /// </summary>
    [Test]
    public async Task A_plain_add_has_no_revision_because_it_has_no_base_row()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("fresh.txt", "new");
        copy.Svn("add", "--quiet", "fresh.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNodes().Single(r => r.RelPath == "fresh.txt");

        await Assert.That(row.LowerOpDepth).IsNull();
        await Assert.That(row.HasCopySource).IsFalse();
        await Assert.That(row.BaseRevision).IsNull();
    }

    /// <summary>
    /// A local add layers a second row over BASE. Reading the wrong one reports the node as it was
    /// before the add.
    /// </summary>
    [Test]
    public async Task A_local_add_is_projected_at_its_highest_op_depth()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Write("fresh.txt", "new");
        copy.Svn("add", "--quiet", "fresh.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNodes().Single(r => r.RelPath == "fresh.txt");

        await Assert.That(row.OpDepth).IsEqualTo(1);
        await Assert.That(row.Presence).IsEqualTo("normal");
        await Assert.That(row.LowerOpDepth).IsNull();
        await Assert.That(row.HasCopySource).IsFalse();
    }

    /// <summary>
    /// The pristine set lives in NODES and the working set in ACTUAL_NODE; reading only the former
    /// made a locally added <c>svn:eol-style</c> look absent, which is the dangerous direction.
    /// </summary>
    [Test]
    public async Task A_locally_added_translating_property_is_seen()
    {
        using var copy = Committed(("readme.txt", "hello"));

        using var before = WcDbReader.Open(copy.Root);
        var clean = before.ReadNodes().Single(r => r.RelPath == "readme.txt");

        copy.Svn("propset", "svn:eol-style", "native", "readme.txt");

        using var after = WcDbReader.Open(copy.Root);
        var translated = after.ReadNodes().Single(r => r.RelPath == "readme.txt");

        await Assert.That(clean.IsTranslated).IsFalse();
        await Assert.That(translated.IsTranslated).IsTrue();
    }

    [Test]
    public async Task A_non_translating_property_keeps_the_checksum_fast_path()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Svn("propset", "svn:needs-lock", "yes", "readme.txt");

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNodes().Single(r => r.RelPath == "readme.txt");

        await Assert.That(row.IsTranslated).IsFalse();
        await Assert.That(row.PropertyStatus).IsEqualTo(PropertyStatus.Modified);
    }

    [Test]
    public async Task Every_versioned_directory_appears_in_the_ignore_map_including_the_root()
    {
        using var copy = Committed(("sub/deep/keep.txt", "hello"));
        copy.Svn("propset", "svn:ignore", "*.tmp", ".");

        using var reader = WcDbReader.Open(copy.Root);
        var patterns = reader.ReadDirectoryIgnorePatterns();

        await Assert
            .That(patterns.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(new[] { string.Empty, "sub", "sub/deep" });
        await Assert
            .That(string.Join('|', patterns[string.Empty].ImmediateChildren))
            .IsEqualTo("*.tmp");
        await Assert.That(patterns["sub"].ImmediateChildren).IsEmpty();
    }

    [Test]
    public async Task A_lock_token_is_read()
    {
        using var copy = Committed(("readme.txt", "hello"));
        copy.Svn("lock", "readme.txt", "-m", "mine");

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadNodes().Single(r => r.RelPath == "readme.txt");

        await Assert.That(row.HasLockToken).IsTrue();
    }

    /// <summary>
    /// Rewrites the real wc.db's <c>user_version</c>. Everything else about the database stays as
    /// Subversion wrote it, so the gate is exercised against a genuine schema.
    /// </summary>
    private static void SetFormat(SvnWorkingCopy copy, int format)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(copy.Root, ".svn", "wc.db"),
                Pooling = false,
            }.ToString()
        );
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {format};";
        command.ExecuteNonQuery();
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
}
