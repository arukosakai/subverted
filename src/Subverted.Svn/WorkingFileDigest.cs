using System.Security.Cryptography;

namespace Subverted.Svn;

/// <summary>
/// SHA-1 of a file on disk, in the form wc.db records — which is the only hash that can be compared
/// against <c>NODES.checksum</c>, however much a newer one would be preferred.
/// </summary>
internal static class WorkingFileDigest
{
    /// <returns>
    /// Lowercase hex, or <see langword="null"/> when the file could not be read. Null means "cannot
    /// decide", never "different": another process holding a file open is an ordinary thing while
    /// an artist is saving, and treating it as a mismatch would report clean files as changed.
    /// </returns>
    public static string? TryCompute(string absolutePath)
    {
        try
        {
            using var stream = File.OpenRead(absolutePath);
            return Convert.ToHexStringLower(SHA1.HashData(stream));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
