namespace Subverted.Svn.Tests;

/// <summary>
/// Hits the real filesystem on purpose. Directory enumeration semantics are exactly what a
/// hand-written fake gets wrong, and did — every versioned directory once reported as missing.
/// </summary>
public sealed class WorkingCopyFileIndexTests
{
    [Test]
    public async Task A_directory_is_indexed_as_a_directory_not_as_absent()
    {
        await WithWorkingCopy(async root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "assets"));

            var index = WorkingCopyFileIndex.Build(root);

            await Assert.That(index.Find("assets")).IsTypeOf<DirectoryNode>();
        });
    }

    [Test]
    public async Task A_file_is_indexed_with_its_length_and_write_time()
    {
        await WithWorkingCopy(async root =>
        {
            var path = Path.Combine(root, "hero.png");
            await File.WriteAllTextAsync(path, "1234567890");

            var snapshot = WorkingCopyFileIndex.Build(root).Find("hero.png") as FileNode;

            await Assert.That(snapshot).IsNotNull();
            await Assert.That(snapshot!.Length).IsEqualTo(10);
            await Assert
                .That(snapshot.LastWriteTimeUtc)
                .IsEqualTo(new FileInfo(path).LastWriteTimeUtc);
        });
    }

    [Test]
    public async Task Nested_paths_are_keyed_with_forward_slashes_to_match_wc_db()
    {
        await WithWorkingCopy(async root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "assets", "characters"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "assets", "characters", "hero.png"),
                "x"
            );

            var index = WorkingCopyFileIndex.Build(root);

            await Assert.That(index.Find("assets/characters/hero.png")).IsTypeOf<FileNode>();
            await Assert.That(index.Find("assets/characters")).IsTypeOf<DirectoryNode>();
        });
    }

    /// <summary>
    /// The admin directory holds thousands of pristine files that are not working-copy content.
    /// Indexing them would be both wrong and the bulk of the walk.
    /// </summary>
    [Test]
    public async Task The_svn_admin_directory_is_not_indexed()
    {
        await WithWorkingCopy(async root =>
        {
            Directory.CreateDirectory(Path.Combine(root, ".svn", "pristine"));
            await File.WriteAllTextAsync(Path.Combine(root, ".svn", "wc.db"), "x");

            var index = WorkingCopyFileIndex.Build(root);

            await Assert.That(index.Find(".svn")).IsNull();
            await Assert.That(index.Find(".svn/wc.db")).IsNull();
        });
    }

    [Test]
    public async Task An_absent_path_is_not_found()
    {
        await WithWorkingCopy(async root =>
        {
            var index = WorkingCopyFileIndex.Build(root);

            await Assert.That(index.Find("never-existed.txt")).IsNull();
        });
    }

    [Test]
    public async Task A_root_that_does_not_exist_yields_an_empty_index_rather_than_throwing()
    {
        await WithWorkingCopy(async root =>
        {
            var index = WorkingCopyFileIndex.Build(Path.Combine(root, "gone"));

            await Assert.That(index.Find("anything")).IsNull();
        });
    }

    /// <summary>
    /// wc.db has a row for the root under the empty relative path, and resolving it needs a
    /// snapshot like any other directory — without one the root reports as Missing.
    /// </summary>
    [Test]
    public async Task The_root_itself_is_indexed_under_the_empty_path()
    {
        await WithWorkingCopy(async root =>
        {
            var index = WorkingCopyFileIndex.Build(root);

            await Assert.That(index.Find(string.Empty)).IsTypeOf<DirectoryNode>();
        });
    }

    [Test]
    public async Task A_root_that_does_not_exist_does_not_even_index_itself()
    {
        await WithWorkingCopy(async root =>
        {
            var index = WorkingCopyFileIndex.Build(Path.Combine(root, "gone"));

            await Assert.That(index.Find(string.Empty)).IsNull();
        });
    }

    /// <summary>
    /// The scanner closes a whole subtree the moment it meets an unversioned directory, which only
    /// works if the directory is handed over before anything inside it. A sibling may still sort
    /// in between — <c>assets.txt</c> precedes <c>assets/</c> because '.' is below '/' — and that
    /// is fine; only parent-before-child is relied on.
    /// </summary>
    [Test]
    public async Task Tree_order_puts_a_directory_before_everything_inside_it()
    {
        await WithWorkingCopy(async root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "assets", "characters"));
            await File.WriteAllTextAsync(Path.Combine(root, "assets", "hero.png"), "x");
            await File.WriteAllTextAsync(
                Path.Combine(root, "assets", "characters", "villain.png"),
                "x"
            );
            await File.WriteAllTextAsync(Path.Combine(root, "assets.txt"), "x");

            var ordered = WorkingCopyFileIndex
                .Build(root)
                .InTreeOrder()
                .Select(entry => entry.Key)
                .ToList();

            await Assert
                .That(string.Join('|', ordered))
                .IsEqualTo(
                    "|assets|assets.txt|assets/characters|assets/characters/villain.png|assets/hero.png"
                );
        });
    }

    private static async Task WithWorkingCopy(Func<string, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"subverted-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await body(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
