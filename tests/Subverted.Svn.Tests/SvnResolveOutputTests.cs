namespace Subverted.Svn.Tests;

/// <summary>
/// Counting these lines is the only way to tell a resolve that did something from one that did not:
/// <c>svn resolve</c> prints nothing and exits zero for a path it had no conflict to settle. Every
/// blob below is what SVN 1.8.15 or 1.14.5 actually wrote to stdout under <c>LC_ALL=C</c>.
/// </summary>
public sealed class SvnResolveOutputTests
{
    private const string ResolvedText = "Resolved conflicted state of 'text.txt'\n";

    private const string ResolvedNested = "Resolved conflicted state of 'sub\\nested.txt'\n";

    [Test]
    public async Task Nothing_on_stdout_is_nothing_resolved()
    {
        await Assert.That(SvnResolveOutput.ResolvedPaths(string.Empty, true)).IsEmpty();
    }

    [Test]
    public async Task A_resolved_node_is_reported_by_the_path_inside_the_quotes()
    {
        var resolved = SvnResolveOutput.ResolvedPaths(ResolvedText, true);

        await Assert.That(resolved.Count).IsEqualTo(1);
        await Assert.That(resolved[0]).IsEqualTo("text.txt");
    }

    /// <summary>
    /// SVN walks its own tree order rather than the order the targets were given, so the order here
    /// is SVN's and the test says so by giving them back to front.
    /// </summary>
    [Test]
    public async Task Several_resolved_nodes_come_back_in_the_order_svn_printed_them()
    {
        var resolved = SvnResolveOutput.ResolvedPaths(
            "Resolved conflicted state of 'asset.bin'\n" + ResolvedText,
            true
        );

        await Assert.That(resolved.Count).IsEqualTo(2);
        await Assert.That(resolved[0]).IsEqualTo("asset.bin");
        await Assert.That(resolved[1]).IsEqualTo("text.txt");
    }

    /// <summary>
    /// SVN echoes the platform separator and every <c>RelPath</c> Subverted reports is
    /// slash-separated. Left alone, <c>sv resolve</c> and <c>sv st</c> name one file two ways.
    /// </summary>
    [Test]
    public async Task On_windows_a_backslash_becomes_the_separator_everything_else_uses()
    {
        var resolved = SvnResolveOutput.ResolvedPaths(ResolvedNested, true);

        await Assert.That(resolved[0]).IsEqualTo("sub/nested.txt");
    }

    /// <summary>
    /// The other side of the same rule: on POSIX a backslash is a legal character in a filename, so
    /// rewriting it would report a file that does not exist.
    /// </summary>
    [Test]
    public async Task Where_a_backslash_is_not_a_separator_it_is_left_in_the_name()
    {
        var resolved = SvnResolveOutput.ResolvedPaths(ResolvedNested, false);

        await Assert.That(resolved[0]).IsEqualTo("sub\\nested.txt");
    }

    /// <summary>
    /// On 1.8 the wording is identical for a text, a property and a tree conflict — SVN does not
    /// say which kind it settled — and on 1.14 it is still the wording for a property conflict.
    /// </summary>
    [Test]
    [Arguments("text.txt")]
    [Arguments("other.txt")]
    [Arguments("sub/nested.txt")]
    public async Task Every_kind_of_conflict_reports_itself_resolved_the_same_way(string relPath)
    {
        var resolved = SvnResolveOutput.ResolvedPaths(
            $"Resolved conflicted state of '{relPath}'\n",
            true
        );

        await Assert.That(resolved.Count).IsEqualTo(1);
        await Assert.That(resolved[0]).IsEqualTo(relPath);
    }

    /// <summary>
    /// A line that merely mentions the words is not a resolution. Inventing one would report a
    /// conflict as settled when it is still there, which is the dangerous direction.
    /// </summary>
    [Test]
    [Arguments("Skipped 'text.txt'\n")]
    [Arguments("  Resolved conflicted state of 'text.txt' and then some\n")]
    [Arguments("svn: warning: W155010: Resolved conflicted state of 'text.txt'\n")]
    public async Task A_line_that_is_not_one_of_these_resolves_nothing(string standardOutput)
    {
        await Assert.That(SvnResolveOutput.ResolvedPaths(standardOutput, true)).IsEmpty();
    }

    /// <summary>svn 1.14 words a settled text conflict differently from 1.8.</summary>
    [Test]
    public async Task A_merge_conflict_marked_resolved_is_a_resolved_node()
    {
        var resolved = SvnResolveOutput.ResolvedPaths(
            "Merge conflicts in 'src\\a.txt' marked as resolved.\r\n",
            true
        );

        await Assert.That(resolved).IsEquivalentTo(["src/a.txt"]);
    }

    [Test]
    public async Task A_tree_conflict_marked_resolved_is_a_resolved_node()
    {
        var resolved = SvnResolveOutput.ResolvedPaths(
            "Tree conflict at 'src\\a.txt' marked as resolved.\r\n",
            true
        );

        await Assert.That(resolved).IsEquivalentTo(["src/a.txt"]);
    }

    /// <summary>
    /// 1.14 announces a node with a text and a property conflict once for each; it is still one
    /// node, and counting it twice would report a resolve of something that is not there.
    /// </summary>
    [Test]
    public async Task A_node_announced_for_two_conflicts_is_one_resolved_node()
    {
        var resolved = SvnResolveOutput.ResolvedPaths(
            "Merge conflicts in 'src\\a.txt' marked as resolved.\r\n"
                + "Resolved conflicted state of 'src\\a.txt'\r\n",
            true
        );

        await Assert.That(resolved).IsEquivalentTo(["src/a.txt"]);
    }

    [Test]
    public async Task Nodes_announced_in_both_wordings_keep_svns_order()
    {
        var resolved = SvnResolveOutput.ResolvedPaths(
            "Merge conflicts in 'art\\h.txt' marked as resolved.\r\n"
                + "Resolved conflicted state of 'src'\r\n"
                + "Tree conflict at 'src\\a.txt' marked as resolved.\r\n",
            true
        );

        await Assert.That(resolved[0]).IsEqualTo("art/h.txt");
        await Assert.That(resolved[1]).IsEqualTo("src");
        await Assert.That(resolved[2]).IsEqualTo("src/a.txt");
        await Assert.That(resolved.Count).IsEqualTo(3);
    }

    /// <summary>The ending is part of the shape, so a quote inside the name cannot cut the path short.</summary>
    [Test]
    public async Task A_quote_inside_the_name_stays_in_the_path()
    {
        var resolved = SvnResolveOutput.ResolvedPaths(
            "Merge conflicts in 'rena's sketch.png' marked as resolved.\n",
            true
        );

        await Assert.That(resolved).IsEquivalentTo(["rena's sketch.png"]);
    }

    [Test]
    [Arguments("Merge conflicts in 'a.txt'\n")]
    [Arguments("Tree conflict at 'a.txt' marked as resolved\n")]
    [Arguments("Merge conflicts in '' marked as resolved.\n")]
    public async Task A_line_missing_part_of_the_newer_shapes_resolves_nothing(
        string standardOutput
    )
    {
        await Assert.That(SvnResolveOutput.ResolvedPaths(standardOutput, true)).IsEmpty();
    }

    [Test]
    [Arguments("Resolved conflicted state of 'a'\n")]
    [Arguments("Merge conflicts in 'a' marked as resolved.\n")]
    public async Task A_one_character_name_is_a_resolved_node(string standardOutput)
    {
        await Assert
            .That(SvnResolveOutput.ResolvedPaths(standardOutput, true))
            .IsEquivalentTo(["a"]);
    }

    [Test]
    public async Task An_empty_path_is_not_a_resolved_node()
    {
        await Assert
            .That(SvnResolveOutput.ResolvedPaths("Resolved conflicted state of ''\n", true))
            .IsEmpty();
    }

    [Test]
    public async Task Windows_line_endings_leave_no_carriage_return_in_the_path()
    {
        var resolved = SvnResolveOutput.ResolvedPaths(ResolvedText.Replace("\n", "\r\n"), true);

        await Assert.That(resolved.Count).IsEqualTo(1);
        await Assert.That(resolved[0]).IsEqualTo("text.txt");
    }

    [Test]
    public async Task A_trailing_newline_does_not_add_an_empty_node()
    {
        await Assert
            .That(SvnResolveOutput.ResolvedPaths(ResolvedText + "\n", true).Count)
            .IsEqualTo(1);
    }
}
