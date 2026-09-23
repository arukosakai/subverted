using System.Text;
using Subverted.Frontend.Diff;

namespace Subverted.Frontend.Tests.Diff;

/// <summary>
/// The <c>svn diff</c> outputs in <c>Diff/Captures</c>, byte for byte as the daemon receives them.
/// Where they came from, and with which client, is in that folder's README.
/// </summary>
internal static class CapturedDiff
{
    public static string Read(string captureName) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Diff", "Captures", captureName),
            Encoding.UTF8
        );

    public static DiffDocument Parse(string captureName) =>
        UnifiedDiffParser.Parse(Read(captureName));

    /// <summary>The capture's one file, failing if it holds any other number.</summary>
    public static FileDiff OnlyFile(string captureName) => Parse(captureName).Files.Single();

    /// <summary>The file at <paramref name="path"/> in a multi-file capture, which must be there once.</summary>
    public static FileDiff FileIn(string captureName, string path) =>
        Parse(captureName).Files.Single(file => file.Path == path);
}
