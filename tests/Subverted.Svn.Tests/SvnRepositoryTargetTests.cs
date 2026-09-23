namespace Subverted.Svn.Tests;

/// <summary>How a repository path becomes the pegged URL <c>svn diff -c N</c> is given.</summary>
public sealed class SvnRepositoryTargetTests
{
    private const string Root = "https://svn.example/repo";

    [Test]
    public async Task A_path_is_joined_to_the_root_and_pegged_at_the_revision()
    {
        await Assert
            .That(SvnTarget.InRepository(Root, "/trunk/art/hero.png", 1824))
            .IsEqualTo("https://svn.example/repo/trunk/art/hero.png@1824");
    }

    [Test]
    public async Task A_root_recorded_with_a_trailing_slash_does_not_double_it()
    {
        await Assert
            .That(SvnTarget.InRepository(Root + "/", "/a.txt", 2))
            .IsEqualTo("https://svn.example/repo/a.txt@2");
    }

    [Test]
    public async Task The_repository_root_itself_is_the_root_url()
    {
        await Assert.That(SvnTarget.InRepository(Root, "/", 7)).IsEqualTo(Root + "@7");
    }

    /// <summary>
    /// A literal <c>%41</c> left unescaped would reach SVN as <c>A</c>, naming a different file.
    /// </summary>
    [Test]
    public async Task A_percent_in_a_name_is_escaped_so_it_is_not_read_as_an_escape()
    {
        await Assert
            .That(SvnTarget.InRepository(Root, "/100%41 done.txt", 9))
            .IsEqualTo("https://svn.example/repo/100%2541%20done.txt@9");
    }

    [Test]
    public async Task An_at_sign_in_a_name_is_escaped_so_only_the_peg_is_read_as_one()
    {
        await Assert
            .That(SvnTarget.InRepository(Root, "/icon@2x.png", 7))
            .IsEqualTo("https://svn.example/repo/icon%402x.png@7");
    }

    [Test]
    public async Task A_name_outside_ascii_is_escaped_as_utf8()
    {
        await Assert
            .That(SvnTarget.InRepository(Root, "/zażółć.txt", 3))
            .IsEqualTo("https://svn.example/repo/za%C5%BC%C3%B3%C5%82%C4%87.txt@3");
    }

    [Test]
    public async Task Svnversion_is_given_the_path_whole_with_no_peg_terminator()
    {
        var root = Path.GetFullPath("/wc");

        await Assert
            .That(SvnTarget.WithinForSvnversion(root, Path.Combine(root, "icon@2x.png")))
            .IsEqualTo("icon@2x.png");
        await Assert.That(SvnTarget.WithinForSvnversion(root, root)).IsEqualTo(".");
    }
}
