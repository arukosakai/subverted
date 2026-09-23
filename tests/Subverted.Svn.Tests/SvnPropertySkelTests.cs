using System.Text;

namespace Subverted.Svn.Tests;

/// <summary>
/// The blobs in <see cref="Blobs_taken_verbatim_from_wc_db"/> were copied byte-for-byte out of a
/// real SVN 1.8.15 working copy. Everything else here is a hand-written variation on that shape —
/// if the real format ever disagrees with these, the real format wins.
/// </summary>
public sealed class SvnPropertySkelTests
{
    [Test]
    [Arguments("(svn:needs-lock 1 *)", "svn:needs-lock=*")]
    [Arguments("(svn:eol-style native)", "svn:eol-style=native")]
    [Arguments("(custom:empty 0 )", "custom:empty=")]
    [Arguments("(svn:ignore 12 build\n*.tmp\n)", "svn:ignore=build\n*.tmp\n")]
    [Arguments(
        "(custom:local pending svn:eol-style native)",
        "custom:local=pending|svn:eol-style=native"
    )]
    [Arguments(
        "(svn:mime-type application/octet-stream custom:a 30 value with spaces and (parens) svn:needs-lock 1 *)",
        "svn:mime-type=application/octet-stream|custom:a=value with spaces and (parens)|svn:needs-lock=*"
    )]
    public async Task Blobs_taken_verbatim_from_wc_db(string blob, string expected)
    {
        await Assert.That(Parse(blob)).IsEqualTo(expected);
    }

    /// <summary>
    /// A checked-out node stores an empty skel where a freshly committed one stores NULL. Both
    /// mean the node has no properties, and conflating either with "unparseable" would push every
    /// checked-out file onto the slow path.
    /// </summary>
    [Test]
    public async Task An_empty_skel_holds_no_properties()
    {
        await Assert.That(Parse("()")).IsEqualTo("");
    }

    [Test]
    public async Task A_null_blob_holds_no_properties()
    {
        await Assert.That(Format(SvnPropertySkel.Parse(null))).IsEqualTo("");
    }

    [Test]
    public async Task An_empty_blob_holds_no_properties()
    {
        await Assert.That(Format(SvnPropertySkel.Parse([]))).IsEqualTo("");
    }

    /// <summary>
    /// The length prefix is what lets a value hold the very bytes that terminate a bare atom, so
    /// each of them has to survive the round trip.
    /// </summary>
    [Test]
    [Arguments("(a 1  )", "a= ")]
    [Arguments("(a 1 ()", "a=(")]
    [Arguments("(a 1 ))", "a=)")]
    [Arguments("(a 1 7)", "a=7")]
    [Arguments("(a 2 \n\t)", "a=\n\t")]
    public async Task An_explicit_length_atom_keeps_bytes_a_bare_atom_could_not_hold(
        string blob,
        string expected
    )
    {
        await Assert.That(Parse(blob)).IsEqualTo(expected);
    }

    /// <summary>
    /// Bare atoms may start with either case, and run up to whitespace or a paren. The single-letter
    /// cases sit on the ends of both alphabet ranges, which is where an off-by-one would hide.
    /// </summary>
    [Test]
    [Arguments("(Name Value)", "Name=Value")]
    [Arguments("(zA aZ)", "zA=aZ")]
    [Arguments("(a z)", "a=z")]
    [Arguments("(A Z)", "A=Z")]
    [Arguments("(a b/c-d:e_f.g)", "a=b/c-d:e_f.g")]
    public async Task A_bare_atom_may_start_with_any_letter(string blob, string expected)
    {
        await Assert.That(Parse(blob)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("  (a b)")]
    [Arguments("(a b)  ")]
    [Arguments("(\ta\nb\r)")]
    [Arguments("(a    b)")]
    public async Task Whitespace_around_and_between_atoms_is_not_part_of_them(string blob)
    {
        await Assert.That(Parse(blob)).IsEqualTo("a=b");
    }

    /// <summary>
    /// Every one of these is a shape wc.db should never contain. Returning null rather than a
    /// best guess is what lets the caller fall back to <c>svn status</c> instead of misreporting.
    /// </summary>
    [Test]
    [Arguments("a b", "no enclosing list")]
    [Arguments("   ", "nothing but whitespace is not a list")]
    [Arguments("(a b", "unterminated list")]
    [Arguments("(a _b)", "an underscore sits just below 'a' and is still not a letter")]
    [Arguments("(a ~b)", "a tilde sits just above 'z' and is still not a letter")]
    [Arguments("(a)", "odd number of atoms")]
    [Arguments("(a b c)", "odd number of atoms with more than one pair's worth")]
    [Arguments("(svn:ignore (build *.tmp))", "a nested list is not a property value")]
    [Arguments("()x", "trailing bytes after the list")]
    [Arguments("(-a b)", "an atom may start with a letter or a digit, nothing else")]
    [Arguments("(a 9 short)", "explicit length runs past the end of the blob")]
    [Arguments("(a 99 short)", "explicit length exceeds the whole blob")]
    [Arguments("(a 99999999999999999999 x)", "a length that would overflow is still just too long")]
    [Arguments("(a 3x abc)", "the length must be followed by whitespace")]
    [Arguments("(a 1", "the blob ends before the length's whitespace")]
    [Arguments("(a 2 b)", "a value that swallows the closing paren leaves the list unterminated")]
    public async Task A_malformed_blob_is_reported_as_unparseable(string blob, string why)
    {
        await Assert
            .That(SvnPropertySkel.Parse(Encoding.UTF8.GetBytes(blob)))
            .IsNull()
            .Because(why);
    }

    /// <summary>
    /// The other side of the "runs past the end" boundary: a length that exactly consumes the rest
    /// of the list is legal, and rejecting it would drop the last property of every blob.
    /// </summary>
    [Test]
    public async Task An_explicit_length_that_exactly_reaches_the_closing_paren_is_accepted()
    {
        await Assert.That(Parse("(a 5 abcde)")).IsEqualTo("a=abcde");
    }

    [Test]
    public async Task Properties_keep_the_order_they_were_stored_in()
    {
        await Assert.That(Parse("(c 1 3 a 1 1 b 1 2)")).IsEqualTo("c=3|a=1|b=2");
    }

    private static string? Parse(string blob) =>
        Format(SvnPropertySkel.Parse(Encoding.UTF8.GetBytes(blob)));

    private static string? Format(IReadOnlyList<SvnProperty>? properties) =>
        properties is null ? null : string.Join('|', properties.Select(p => $"{p.Name}={p.Value}"));
}
