using System.Text;

namespace Subverted.Svn.Tests;

public sealed class SvnTextSpellingTests
{
    [Test]
    public async Task On_windows_svn_is_taken_to_write_the_ansi_code_page()
    {
        await Assert.That(SvnTextSpelling.For(isWindows: true)).IsTypeOf<AnsiCodePageSpelling>();
    }

    [Test]
    public async Task Elsewhere_svn_is_taken_to_write_utf8()
    {
        await Assert.That(SvnTextSpelling.For(isWindows: false)).IsTypeOf<Utf8Spelling>();
    }

    [Test]
    public async Task Utf8_loses_no_name_and_spells_each_as_itself()
    {
        var spelling = new Utf8Spelling();

        await Assert.That(spelling.CanLoseNames).IsFalse();
        await Assert.That(spelling.AsSvnWouldPrint("ドラゴン.txt")).IsEqualTo("ドラゴン.txt");
        await Assert.That(spelling.LinesThatAreNotUtf8).IsEqualTo(Encoding.UTF8);
    }

    /// <summary>Plain ASCII is the same in every ANSI code page, whichever this machine has.</summary>
    [Test]
    public async Task An_ascii_name_reaches_svns_text_unchanged_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await Assert
            .That(new AnsiCodePageSpelling().AsSvnWouldPrint("art/hero.png"))
            .IsEqualTo("art/hero.png");
    }

    [Test]
    public async Task An_empty_name_is_spelled_empty_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await Assert.That(new AnsiCodePageSpelling().AsSvnWouldPrint(string.Empty)).IsEmpty();
    }
}
