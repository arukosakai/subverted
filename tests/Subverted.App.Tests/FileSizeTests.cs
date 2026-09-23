using System.Globalization;
using Subverted.App.Presentation;

namespace Subverted.App.Tests;

public sealed class FileSizeTests
{
    private const long Kilo = 1024;

    [Test]
    [Arguments(0L, "0 bytes")]
    [Arguments(1L, "1 byte")]
    [Arguments(2L, "2 bytes")]
    [Arguments(1023L, "1023 bytes")]
    [Arguments(1024L, "1 KB")]
    [Arguments(1536L, "1.5 KB")]
    [Arguments(1023 * Kilo, "1023 KB")]
    [Arguments(Kilo * Kilo - 1, "1 MB")]
    [Arguments(Kilo * Kilo, "1 MB")]
    [Arguments(5 * Kilo * Kilo + 300 * Kilo, "5.3 MB")]
    [Arguments(3 * Kilo * Kilo * Kilo / 2, "1.5 GB")]
    [Arguments(2 * Kilo * Kilo * Kilo * Kilo, "2 TB")]
    [Arguments(2048 * Kilo * Kilo * Kilo * Kilo, "2048 TB")]
    public async Task A_size_reads_in_the_largest_unit_it_fills(long bytes, string text)
    {
        await Assert.That(FileSize.Format(bytes, CultureInfo.InvariantCulture)).IsEqualTo(text);
    }

    /// <summary>Built by hand: the suite runs globalization-invariant, where named cultures do not exist.</summary>
    public static CultureInfo DecimalComma()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        return culture;
    }

    [Test]
    public async Task A_size_is_written_in_the_persons_own_number_format()
    {
        await Assert.That(FileSize.Format(1536, DecimalComma())).IsEqualTo("1,5 KB");
    }
}
