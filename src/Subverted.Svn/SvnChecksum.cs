namespace Subverted.Svn;

/// <summary>
/// Parses the checksum format wc.db stores in <c>NODES.checksum</c>, which is the algorithm name
/// wrapped in dollar signs followed by lowercase hex — <c>$sha1$7db3de1c…</c>.
/// </summary>
internal static class SvnChecksum
{
    private const string Sha1Prefix = "$sha1$";
    private const int Sha1HexLength = 40;

    /// <returns>
    /// The lowercase hex digest, or <see langword="null"/> if this is not a SHA-1 checksum we
    /// recognise. Callers treat null as "cannot decide" rather than as "not equal".
    /// </returns>
    public static string? TryParseSha1(string? raw)
    {
        if (raw is null || !raw.StartsWith(Sha1Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var digest = raw[Sha1Prefix.Length..];
        return digest.Length == Sha1HexLength && digest.All(IsLowercaseHex) ? digest : null;
    }

    private static bool IsLowercaseHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';
}
