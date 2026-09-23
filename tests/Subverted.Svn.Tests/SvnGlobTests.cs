namespace Subverted.Svn.Tests;

/// <summary>
/// The patterns here are the ones SVN 1.8.15 actually ships or that the ignore fixture proved out;
/// the expected answers are what <c>svn status</c> gave for a file of that name.
/// </summary>
public sealed class SvnGlobTests
{
    [Test]
    [Arguments("build", "build", true)]
    [Arguments("build", "builds", false)]
    [Arguments("build", "uild", false)]
    [Arguments("*.tmp", "root.tmp", true)]
    [Arguments("*.tmp", "tmp", false)]
    [Arguments("*.tmp", "a.tmp.bak", false)]
    public async Task Literal_text_must_match_exactly(string pattern, string name, bool expected)
    {
        await Assert.That(SvnGlob.Matches(pattern, name)).IsEqualTo(expected);
    }

    /// <summary>
    /// SVN's own config file documents this: "'*' matches leading dots, e.g. '*.rej' matches
    /// '.foo.rej'". Plain fnmatch does not, so this is a real difference and not an accident.
    /// </summary>
    [Test]
    [Arguments("*.rej", ".foo.rej", true)]
    [Arguments("*~", "backup~", true)]
    [Arguments("#*#", "#emacs#", true)]
    [Arguments(".#*", ".#lock", true)]
    [Arguments(".*.swp", ".t.swp", true)]
    public async Task A_star_matches_a_leading_dot(string pattern, string name, bool expected)
    {
        await Assert.That(SvnGlob.Matches(pattern, name)).IsEqualTo(expected);
    }

    /// <summary>
    /// `CASE.TMP` sat unignored next to an ignored `root.tmp` on Windows, so this is not a
    /// platform detail we get to smooth over.
    /// </summary>
    [Test]
    [Arguments("*.tmp", "CASE.TMP", false)]
    [Arguments("*.TMP", "case.tmp", false)]
    [Arguments("*.tmp", "case.tmp", true)]
    public async Task Matching_is_case_sensitive_even_on_windows(
        string pattern,
        string name,
        bool expected
    )
    {
        await Assert.That(SvnGlob.Matches(pattern, name)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("q?x.tmp", "qAx.tmp", true)]
    [Arguments("q?x.tmp", "q1x.tmp", true)]
    [Arguments("q?x.tmp", "qx.tmp", false)]
    [Arguments("q?x.tmp", "qABx.tmp", false)]
    public async Task A_question_mark_takes_exactly_one_character(
        string pattern,
        string name,
        bool expected
    )
    {
        await Assert.That(SvnGlob.Matches(pattern, name)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("set[0-9].dat", "set7.dat", true)]
    [Arguments("set[0-9].dat", "setX.dat", false)]
    [Arguments("[0-9]", "0", true)]
    [Arguments("[0-9]", "9", true)]
    [Arguments("[0-9]", "/", false)]
    [Arguments("[0-9]", ":", false)]
    [Arguments("[abc]", "b", true)]
    [Arguments("[abc]", "d", false)]
    [Arguments("[0-9a]", "a", true)]
    [Arguments("[0-9a]", "b", false)]
    public async Task A_class_matches_any_one_of_its_members(
        string pattern,
        string name,
        bool expected
    )
    {
        await Assert.That(SvnGlob.Matches(pattern, name)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("[!0-9].txt", "a.txt", true)]
    [Arguments("[!0-9].txt", "7.txt", false)]
    [Arguments("[^0-9].txt", "a.txt", true)]
    [Arguments("[^0-9].txt", "7.txt", false)]
    public async Task A_class_can_be_negated_with_either_marker(
        string pattern,
        string name,
        bool expected
    )
    {
        await Assert.That(SvnGlob.Matches(pattern, name)).IsEqualTo(expected);
    }

    /// <summary>
    /// A <c>]</c> in the first slot is a member rather than the terminator, and a <c>-</c> with
    /// nothing after it is a literal — both are fnmatch rules a hand-written pattern will rely on.
    /// </summary>
    [Test]
    [Arguments("[]]", "]", true)]
    [Arguments("[]]", "a", false)]
    [Arguments("[!]]", "a", true)]
    [Arguments("[!]]", "]", false)]
    [Arguments("[a-]", "a", true)]
    [Arguments("[a-]", "-", true)]
    [Arguments("[a-]", "b", false)]
    [Arguments("[-a]", "-", true)]
    public async Task Brackets_and_dashes_in_awkward_places_are_members(
        string pattern,
        string name,
        bool expected
    )
    {
        await Assert.That(SvnGlob.Matches(pattern, name)).IsEqualTo(expected);
    }

    /// <summary>An unclosed class is a literal bracket, not a rejected pattern.</summary>
    [Test]
    [Arguments("[abc", "[abc", true)]
    [Arguments("[abc", "a", false)]
    [Arguments("[", "[", true)]
    [Arguments("[!", "[!", true)]
    [Arguments("[", "a", false)]
    public async Task An_unterminated_class_is_a_literal_bracket(
        string pattern,
        string name,
        bool expected
    )
    {
        await Assert.That(SvnGlob.Matches(pattern, name)).IsEqualTo(expected);
    }

    /// <summary>
    /// `*.so.[0-9]*` is in SVN's default list and is the pattern that needs a star to give back
    /// characters it already consumed.
    /// </summary>
    [Test]
    [Arguments("*.so.[0-9]*", "t.so.6", true)]
    [Arguments("*.so.[0-9]*", "t.so.61", true)]
    [Arguments("*.so.[0-9]*", "t.so.x", false)]
    [Arguments("*.so.[0-9]*", "t.so.", false)]
    [Arguments("a*b*c", "axxbyyc", true)]
    [Arguments("a*b*c", "abc", true)]
    [Arguments("a*b*c", "axxbyy", false)]
    [Arguments("*a*a*a", "aaaa", true)]
    public async Task A_star_gives_back_characters_when_the_rest_fails(
        string pattern,
        string name,
        bool expected
    )
    {
        await Assert.That(SvnGlob.Matches(pattern, name)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("*", "anything", true)]
    [Arguments("*", "", true)]
    [Arguments("**", "ab", true)]
    [Arguments("a*", "a", true)]
    [Arguments("a", "ab", false)]
    [Arguments("ab", "a", false)]
    [Arguments("", "", true)]
    [Arguments("", "a", false)]
    public async Task Exhausting_one_side_before_the_other_decides_the_match(
        string pattern,
        string name,
        bool expected
    )
    {
        await Assert.That(SvnGlob.Matches(pattern, name)).IsEqualTo(expected);
    }
}
