namespace Subverted.Svn.Tests;

public sealed class SvnLocaleTests
{
    /// <summary>svn on Windows takes its charset from code pages, so the locale only picks the language.</summary>
    [Test]
    public async Task On_windows_the_plain_c_locale_keeps_svn_in_english()
    {
        await Assert.That(SvnLocale.LcAllFor(isWindows: true, isMacOS: false)).IsEqualTo("C");
    }

    /// <summary>
    /// Plain C is ASCII there, and svn refuses to read a directory holding any other name
    /// (E000022), so every command on such a working copy would fail.
    /// </summary>
    [Test]
    public async Task On_linux_the_locale_is_english_with_a_utf8_charset()
    {
        await Assert
            .That(SvnLocale.LcAllFor(isWindows: false, isMacOS: false))
            .IsEqualTo("C.UTF-8");
    }

    [Test]
    public async Task On_macos_the_locale_is_one_every_mac_ships_with_a_utf8_charset()
    {
        await Assert
            .That(SvnLocale.LcAllFor(isWindows: false, isMacOS: true))
            .IsEqualTo("en_US.UTF-8");
    }
}
