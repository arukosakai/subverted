using System.Text;

namespace Subverted.Svn.Tests;

/// <summary>
/// svn on Windows writes paths in the ANSI code page — every current build, measured in D34 — while
/// a diff's content lines are the file's own bytes, so each line is decoded on its own.
/// </summary>
public sealed class SvnOutputTextTests
{
    private static readonly Encoding CentralEuropean =
        CodePagesEncodingProvider.Instance.GetEncoding(1250)!;

    [Test]
    public async Task Nothing_decodes_to_nothing()
    {
        await Assert.That(SvnOutputText.Decode([], CentralEuropean)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task A_utf8_line_is_read_as_utf8()
    {
        var bytes = Encoding.UTF8.GetBytes("Index: zażółć.txt\r\n");

        await Assert
            .That(SvnOutputText.Decode(bytes, CentralEuropean))
            .IsEqualTo("Index: zażółć.txt\r\n");
    }

    /// <summary>The bytes SlikSVN 1.14.5 wrote for this name on a Polish Windows.</summary>
    [Test]
    public async Task A_line_that_is_not_utf8_is_read_in_the_other_encoding()
    {
        byte[] bytes = [.. "Index: za"u8, 0xbf, 0xf3, 0xb3, 0xe6, .. ".txt\r\n"u8];

        await Assert
            .That(SvnOutputText.Decode(bytes, CentralEuropean))
            .IsEqualTo("Index: zażółć.txt\r\n");
    }

    /// <summary>
    /// A header in the code page above content that is UTF-8: decoding the whole output either way
    /// would garble one of them.
    /// </summary>
    [Test]
    public async Task Each_line_is_decoded_on_its_own()
    {
        byte[] bytes =
        [
            .. "Index: za"u8,
            0xbf,
            .. ".txt\n"u8,
            .. Encoding.UTF8.GetBytes("+żółw\n"),
        ];

        await Assert
            .That(SvnOutputText.Decode(bytes, CentralEuropean))
            .IsEqualTo("Index: zaż.txt\n+żółw\n");
    }

    [Test]
    public async Task A_last_line_without_a_newline_is_kept()
    {
        byte[] bytes = [.. "one\ntwo"u8];

        await Assert.That(SvnOutputText.Decode(bytes, CentralEuropean)).IsEqualTo("one\ntwo");
    }

    [Test]
    public async Task A_last_line_that_is_not_utf8_is_read_in_the_other_encoding_too()
    {
        byte[] bytes = [.. "one\nza"u8, 0xbf];

        await Assert.That(SvnOutputText.Decode(bytes, CentralEuropean)).IsEqualTo("one\nzaż");
    }
}
