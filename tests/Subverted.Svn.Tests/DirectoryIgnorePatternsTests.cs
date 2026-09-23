using System.Text;

namespace Subverted.Svn.Tests;

/// <summary>
/// Skels are assembled here rather than hand-written, because the encoding is
/// <see cref="SvnPropertySkelTests"/>'s subject; what matters here is how a parsed value is split
/// into patterns.
/// </summary>
public sealed class DirectoryIgnorePatternsTests
{
    /// <summary>
    /// A space-separated `svn:ignore` on SVN 1.8.15 ignored only a file literally named
    /// "sp1.txt sp2.txt", leaving sp1.txt and sp2.txt reported — so newline is the one delimiter.
    /// </summary>
    [Test]
    public async Task Svn_ignore_splits_on_newlines_and_not_on_spaces()
    {
        var patterns = From(("svn:ignore", "sp1.txt sp2.txt\n"));

        await Assert.That(Join(patterns.ImmediateChildren)).IsEqualTo("sp1.txt sp2.txt");
    }

    /// <summary>
    /// The documentation calls this one whitespace-delimited, like its config-file namesake. On
    /// 1.8.15 it is not: a space-separated value ignored nothing.
    /// </summary>
    [Test]
    public async Task Svn_global_ignores_splits_on_newlines_too()
    {
        var patterns = From(("svn:global-ignores", "g1.txt\ng2.txt\n"));

        await Assert.That(Join(patterns.Descendants)).IsEqualTo("g1.txt|g2.txt");
    }

    [Test]
    public async Task The_two_properties_land_on_different_reaches()
    {
        var patterns = From(("svn:global-ignores", "deep.xyz\n"), ("svn:ignore", "*.tmp\n"));

        await Assert.That(Join(patterns.ImmediateChildren)).IsEqualTo("*.tmp");
        await Assert.That(Join(patterns.Descendants)).IsEqualTo("deep.xyz");
    }

    [Test]
    public async Task Carriage_returns_do_not_become_part_of_a_pattern()
    {
        var patterns = From(("svn:ignore", "a.txt\r\nb.txt\r\n"));

        await Assert.That(Join(patterns.ImmediateChildren)).IsEqualTo("a.txt|b.txt");
    }

    [Test]
    public async Task Blank_lines_between_patterns_are_not_patterns()
    {
        var patterns = From(("svn:ignore", "a.txt\n\n\nb.txt\n"));

        await Assert.That(Join(patterns.ImmediateChildren)).IsEqualTo("a.txt|b.txt");
    }

    /// <summary>
    /// Every asset directory carries properties that have nothing to do with ignoring; picking one
    /// of those up as a pattern would hide real files.
    /// </summary>
    [Test]
    public async Task Properties_that_are_not_ignore_properties_contribute_nothing()
    {
        var patterns = From(("svn:needs-lock", "*"), ("svn:mime-type", "text/plain"));

        await Assert.That(Join(patterns.ImmediateChildren)).IsEqualTo("");
        await Assert.That(Join(patterns.Descendants)).IsEqualTo("");
    }

    [Test]
    public async Task A_directory_with_no_properties_declares_nothing()
    {
        var patterns = DirectoryIgnorePatterns.FromProperties(null);

        await Assert.That(Join(patterns.ImmediateChildren)).IsEqualTo("");
        await Assert.That(Join(patterns.Descendants)).IsEqualTo("");
    }

    /// <summary>
    /// Declining to guess here under-ignores, which shows the user extra files. Guessing the other
    /// way would hide them.
    /// </summary>
    [Test]
    public async Task An_unreadable_skel_declares_nothing()
    {
        var patterns = DirectoryIgnorePatterns.FromProperties(
            Encoding.UTF8.GetBytes("(svn:ignore 6 *.tmp")
        );

        await Assert.That(Join(patterns.ImmediateChildren)).IsEqualTo("");
        await Assert.That(Join(patterns.Descendants)).IsEqualTo("");
    }

    [Test]
    public async Task Only_svn_ignore_leaves_the_descendant_reach_empty()
    {
        var patterns = From(("svn:ignore", "*.tmp\n"));

        await Assert.That(Join(patterns.ImmediateChildren)).IsEqualTo("*.tmp");
        await Assert.That(Join(patterns.Descendants)).IsEqualTo("");
    }

    [Test]
    public async Task Only_svn_global_ignores_leaves_the_immediate_reach_empty()
    {
        var patterns = From(("svn:global-ignores", "*.tmp\n"));

        await Assert.That(Join(patterns.ImmediateChildren)).IsEqualTo("");
        await Assert.That(Join(patterns.Descendants)).IsEqualTo("*.tmp");
    }

    private static DirectoryIgnorePatterns From(params (string Name, string Value)[] properties)
    {
        var skel = new StringBuilder("(");
        foreach (var (name, value) in properties)
        {
            skel.Append(name)
                .Append(' ')
                .Append(Encoding.UTF8.GetByteCount(value))
                .Append(' ')
                .Append(value)
                .Append(' ');
        }

        return DirectoryIgnorePatterns.FromProperties(
            Encoding.UTF8.GetBytes(skel.Append(')').ToString())
        );
    }

    private static string Join(IReadOnlyList<string> patterns) => string.Join('|', patterns);
}
