using System.Text;

namespace Subverted.Svn.Tests;

public sealed class SvnPropertiesTests
{
    /// <summary>
    /// The whole point of parsing the skel: svn:needs-lock is on most of a game studio's assets,
    /// and it does not change a single byte on disk. Treating it as translating cost every locked
    /// file its checksum fast path.
    /// </summary>
    [Test]
    [Arguments("(svn:needs-lock 1 *)")]
    [Arguments("(svn:mime-type application/octet-stream)")]
    [Arguments("(svn:executable 1 *)")]
    [Arguments("(custom:a 1 b)")]
    [Arguments("(svn:mime-type application/octet-stream svn:needs-lock 1 *)")]
    public async Task A_property_that_does_not_change_the_bytes_on_disk_leaves_the_file_comparable(
        string skel
    )
    {
        await Assert.That(SvnProperties.IsTranslated(Encoding.UTF8.GetBytes(skel))).IsFalse();
    }

    [Test]
    [Arguments("(svn:eol-style native)")]
    [Arguments("(svn:keywords 6 Id Rev)")]
    [Arguments("(svn:special 1 *)")]
    public async Task A_property_that_rewrites_the_working_file_makes_it_incomparable(string skel)
    {
        await Assert.That(SvnProperties.IsTranslated(Encoding.UTF8.GetBytes(skel))).IsTrue();
    }

    /// <summary>
    /// Guards the scan rather than just the first entry: a translating property hidden behind
    /// harmless ones must still be found.
    /// </summary>
    [Test]
    public async Task A_translating_property_is_found_behind_harmless_ones()
    {
        var skel = "(svn:needs-lock 1 * svn:mime-type text/plain svn:eol-style native)";

        await Assert.That(SvnProperties.IsTranslated(Encoding.UTF8.GetBytes(skel))).IsTrue();
    }

    /// <summary>
    /// A checked-out node stores an empty skel where a freshly committed one stores NULL. Reading
    /// the first as "has properties" once leaked 1022 of 20000 nodes onto the slow path.
    /// </summary>
    [Test]
    public async Task An_empty_skel_leaves_the_file_comparable()
    {
        await Assert.That(SvnProperties.IsTranslated(Encoding.UTF8.GetBytes("()"))).IsFalse();
    }

    [Test]
    public async Task A_null_blob_leaves_the_file_comparable()
    {
        await Assert.That(SvnProperties.IsTranslated(null)).IsFalse();
    }

    /// <summary>
    /// A blob we cannot read might be hiding svn:eol-style, and calling a translated file modified
    /// is the one error that matters here. Unreadable therefore means incomparable, not harmless.
    /// </summary>
    [Test]
    [Arguments("(svn:eol-style")]
    [Arguments("not a skel")]
    public async Task An_unparseable_blob_is_treated_as_incomparable(string skel)
    {
        await Assert.That(SvnProperties.IsTranslated(Encoding.UTF8.GetBytes(skel))).IsTrue();
    }
}
