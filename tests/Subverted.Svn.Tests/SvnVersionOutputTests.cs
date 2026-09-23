using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// What <c>svnversion</c> 1.8.15 printed on the <c>subverted-history</c> fixture's checkouts, read
/// back as a range. Every shape here was printed by the real binary.
/// </summary>
public sealed class SvnVersionOutputTests
{
    [Test]
    [Arguments("9\r\n", 9L, 9L)]
    [Arguments("3:5\r\n", 3L, 5L)]
    [Arguments("5:9S\r\n", 5L, 9L)]
    [Arguments("1:9P\r\n", 1L, 9L)]
    [Arguments("9P\r\n", 9L, 9L)]
    [Arguments("7M\r\n", 7L, 7L)]
    [Arguments("4:5MSP\n", 4L, 5L)]
    public async Task A_revision_or_range_reads_as_its_two_ends_whatever_flags_follow(
        string printed,
        long lowest,
        long highest
    )
    {
        await Assert
            .That(SvnVersionOutput.Parse(printed))
            .IsEqualTo(new BaseRevisionRange(lowest, highest));
    }

    [Test]
    [Arguments("Uncommitted local addition, copy or move\r\n")]
    [Arguments("Unversioned file\r\n")]
    [Arguments("Unversioned directory\r\n")]
    [Arguments("Unversioned symlink\r\n")]
    public async Task A_sentence_for_a_path_with_no_base_reads_as_no_range(string printed)
    {
        await Assert.That(SvnVersionOutput.Parse(printed)).IsNull();
    }

    [Test]
    [Arguments("exported\r\n")]
    [Arguments("9X\r\n")]
    [Arguments(":5\r\n")]
    [Arguments("\r\n")]
    public async Task Anything_else_is_refused_rather_than_guessed_at(string printed)
    {
        await Assert
            .That(() => SvnVersionOutput.Parse(printed))
            .Throws<SvnCommandException>()
            .WithMessageContaining("not a range");
    }
}
