namespace Subverted.Svn.Tests;

public sealed class SvnChecksumTests
{
    private const string ValidDigest = "7db3de1c97981e995c3b7e8caf7ca7880ca4caf3";

    [Test]
    public async Task A_sha1_checksum_parses_to_its_digest()
    {
        await Assert.That(SvnChecksum.TryParseSha1($"$sha1${ValidDigest}")).IsEqualTo(ValidDigest);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(ValidDigest)]
    [Arguments("$md5 $a6632de9c737a64c905a6e193f61ff6a")]
    [Arguments("$sha256$7db3de1c97981e995c3b7e8caf7ca7880ca4caf3")]
    public async Task Anything_that_is_not_a_sha1_checksum_is_undecidable(string? raw)
    {
        await Assert.That(SvnChecksum.TryParseSha1(raw)).IsNull();
    }

    [Test]
    [Arguments("7db3de1c97981e995c3b7e8caf7ca7880ca4caf")]
    [Arguments("7db3de1c97981e995c3b7e8caf7ca7880ca4caf33")]
    public async Task A_digest_of_the_wrong_length_is_rejected(string digest)
    {
        await Assert.That(SvnChecksum.TryParseSha1($"$sha1${digest}")).IsNull();
    }

    /// <summary>
    /// SVN writes lowercase hex. Accepting uppercase would make the comparison in
    /// <see cref="PristineComparer"/> silently case-sensitive against a value we never produce.
    /// </summary>
    [Test]
    public async Task Uppercase_hex_is_rejected()
    {
        await Assert
            .That(SvnChecksum.TryParseSha1("$sha1$7DB3DE1C97981E995C3B7E8CAF7CA7880CA4CAF3"))
            .IsNull();
    }

    [Test]
    public async Task Non_hex_characters_are_rejected()
    {
        await Assert
            .That(SvnChecksum.TryParseSha1("$sha1$zzz3de1c97981e995c3b7e8caf7ca7880ca4caf3"))
            .IsNull();
    }
}
