namespace Subverted.Svn.Tests;

/// <summary>
/// The fallback's answer to "what working copy is this" is parsed, not guessed, so the shapes it
/// accepts and refuses are pinned here rather than needing a checkout in each one.
/// </summary>
/// <remarks>
/// <see cref="A_real_info_document_is_read_whole"/> holds a document copied byte for byte out of
/// <c>svn info --xml</c> on 1.8.15. If it and any other test here disagree, the real one wins.
/// </remarks>
public sealed class SvnInfoXmlTests
{
    private const string RealDocument = """
        <?xml version="1.0" encoding="UTF-8"?>
        <info>
        <entry
           kind="dir"
           path="C:\Temp\subverted-fx1\wc"
           revision="0">
        <url>file:///C:/Temp/subverted-fx1/repo</url>
        <relative-url>^/</relative-url>
        <repository>
        <root>file:///C:/Temp/subverted-fx1/repo</root>
        <uuid>88253c39-5246-694b-9601-62044672e415</uuid>
        </repository>
        <wc-info>
        <wcroot-abspath>C:/Temp/subverted-fx1/wc</wcroot-abspath>
        <schedule>normal</schedule>
        <depth>infinity</depth>
        </wc-info>
        <commit
           revision="0">
        <date>2026-09-18T12:54:00.601688Z</date>
        </commit>
        </entry>
        </info>
        """;

    [Test]
    public async Task A_real_info_document_is_read_whole()
    {
        var info = SvnInfoXml.Parse(RealDocument);

        await Assert.That(info.RootPath).IsEqualTo("C:/Temp/subverted-fx1/wc");
        await Assert.That(info.RepositoryRoot).IsEqualTo("file:///C:/Temp/subverted-fx1/repo");
        await Assert.That(info.RepositoryUuid).IsEqualTo("88253c39-5246-694b-9601-62044672e415");
    }

    /// <summary>
    /// The client cannot report wc.db's schema version, and a number invented here would claim the
    /// fast path had read a format it had in fact refused.
    /// </summary>
    [Test]
    public async Task The_schema_format_is_absent_rather_than_assumed()
    {
        await Assert.That(SvnInfoXml.Parse(RealDocument).Format).IsNull();
    }

    /// <summary>
    /// The root SVN reports is the working copy's, not the path that was asked about — which is
    /// what makes one session serve every path under it.
    /// </summary>
    [Test]
    public async Task The_root_is_the_working_copys_own_however_deep_the_path_asked_about_was()
    {
        var info = SvnInfoXml.Parse(
            RealDocument.Replace(
                @"path=""C:\Temp\subverted-fx1\wc""",
                @"path=""C:\Temp\subverted-fx1\wc\assets\hero.png"""
            )
        );

        await Assert.That(info.RootPath).IsEqualTo("C:/Temp/subverted-fx1/wc");
    }

    [Test]
    public async Task A_document_svn_did_not_write_as_xml_says_so()
    {
        await Assert
            .That(() => SvnInfoXml.Parse("svn: E155007: not a working copy"))
            .Throws<SvnCommandException>()
            .WithMessageContaining("not XML");
    }

    [Test]
    public async Task A_document_that_is_not_an_info_document_says_which_one_it_is()
    {
        await Assert
            .That(() => SvnInfoXml.Parse("<status></status>"))
            .Throws<SvnCommandException>()
            .WithMessageContaining("<status> document");
    }

    [Test]
    public async Task An_info_document_with_no_entry_fails_rather_than_answering_about_nothing()
    {
        await Assert
            .That(() => SvnInfoXml.Parse("<info></info>"))
            .Throws<SvnCommandException>()
            .WithMessageContaining("<entry>");
    }

    /// <summary>
    /// Each of these is a part the daemon cannot do without, so a document missing one fails by
    /// name instead of handing back an empty string that reads as a real answer.
    /// </summary>
    [Test]
    [Arguments("wcroot-abspath", "wc-info/wcroot-abspath")]
    [Arguments("wc-info", "wc-info/wcroot-abspath")]
    [Arguments("root", "repository/root")]
    [Arguments("uuid", "repository/uuid")]
    [Arguments("repository", "<repository>")]
    public async Task A_document_missing_a_part_the_daemon_needs_names_it(
        string element,
        string complaint
    )
    {
        await Assert
            .That(() => SvnInfoXml.Parse(Without(RealDocument, element)))
            .Throws<SvnCommandException>()
            .WithMessageContaining(complaint);
    }

    /// <summary>Renames an element so it is no longer the one being looked for.</summary>
    private static string Without(string document, string element) =>
        document.Replace($"<{element}>", "<absent>").Replace($"</{element}>", "</absent>");
}
