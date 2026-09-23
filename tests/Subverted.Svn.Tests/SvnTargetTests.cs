namespace Subverted.Svn.Tests;

public sealed class SvnTargetTests
{
    private static readonly string Root = Path.GetFullPath("/wc");

    /// <summary>
    /// The root is the common case — <c>sv d</c> with no path at all — and <c>svn</c> given an
    /// absolute root prints absolute paths in every header it writes.
    /// </summary>
    [Test]
    public async Task The_root_itself_is_the_current_directory()
    {
        await Assert.That(SvnTarget.Within(Root, Root)).IsEqualTo(".");
    }

    [Test]
    public async Task A_path_inside_the_working_copy_is_written_relative_to_it()
    {
        var inside = Path.Combine(Root, "art", "hero.png");

        await Assert
            .That(SvnTarget.Within(Root, inside))
            .IsEqualTo(Path.Combine("art", "hero.png"));
    }

    /// <summary>
    /// Left as the platform spells it. What SVN prints does not depend on this — it re-spells
    /// every path natively whichever separator it was given, which was found by running it — so
    /// rewriting here would buy nothing and could mangle a Unix filename containing a backslash.
    /// </summary>
    [Test]
    public async Task The_platforms_own_separator_is_left_as_it_is()
    {
        var inside = Path.Combine(Root, "art", "hero.png");

        await Assert
            .That(SvnTarget.Within(Root, inside))
            .Contains(Path.DirectorySeparatorChar.ToString());
    }

    /// <summary>
    /// <c>icon@2x.png</c> is what a retina asset is called, and without the terminator <c>svn</c>
    /// reads <c>2x.png</c> as a revision and refuses the whole command.
    /// </summary>
    [Test]
    [Arguments("icon@2x.png")]
    [Arguments("@lead.png")]
    [Arguments("trail@.png")]
    [Arguments("two@at@signs.png")]
    public async Task A_name_svn_would_split_at_an_at_sign_is_terminated(string name)
    {
        var inside = Path.Combine(Root, "art", name);

        await Assert
            .That(SvnTarget.Within(Root, inside))
            .IsEqualTo(Path.Combine("art", name) + "@");
    }

    [Test]
    public async Task A_name_with_no_at_sign_in_it_is_left_alone()
    {
        var inside = Path.Combine(Root, "art", "hero.png");

        await Assert
            .That(SvnTarget.Within(Root, inside))
            .IsEqualTo(Path.Combine("art", "hero.png"));
    }

    /// <summary>
    /// The terminator must not reach the root's own target: <c>.@</c> is an error in its own right
    /// (<c>E125001</c>), and a checkout in a folder called <c>build@2</c> would otherwise fail
    /// every command Subverted runs.
    /// </summary>
    [Test]
    public async Task A_root_whose_own_name_has_an_at_sign_is_still_just_the_current_directory()
    {
        var root = Path.GetFullPath("/build@2/wc");

        await Assert.That(SvnTarget.Within(root, root)).IsEqualTo(".");
    }

    /// <summary>
    /// <c>svn diff</c> is the exception and takes the name as written; the terminator every other
    /// subcommand needs would be read here as part of the filename.
    /// </summary>
    [Test]
    [Arguments("icon@2x.png")]
    [Arguments("hero.png")]
    public async Task A_diff_target_is_written_exactly_as_the_file_is_named(string name)
    {
        var inside = Path.Combine(Root, "art", name);

        await Assert
            .That(SvnTarget.WithinForDiff(Root, inside))
            .IsEqualTo(Path.Combine("art", name));
    }

    [Test]
    public async Task A_diff_target_for_the_root_is_the_current_directory_too()
    {
        await Assert.That(SvnTarget.WithinForDiff(Root, Root)).IsEqualTo(".");
    }
}
