using System.Xml;
using System.Xml.Linq;

namespace Subverted.Svn;

/// <summary>
/// Reads the paths out of <c>svn diff --summarize --xml</c>, which every build writes in UTF-8 —
/// so it names files exactly where svn's text output may not (D34).
/// </summary>
public static class SvnDiffSummary
{
    /// <returns>
    /// Each path as svn named it, slash-separated. A working-copy diff names them relative to where
    /// svn ran; a revision diff names whole URLs, returned unescaped.
    /// </returns>
    /// <exception cref="SvnCommandException">The text is not XML.</exception>
    public static IReadOnlyList<string> Paths(string xml)
    {
        XElement diff;
        try
        {
            diff = XElement.Parse(xml);
        }
        catch (XmlException exception)
        {
            throw new SvnCommandException(
                $"svn diff --summarize wrote something that is not XML: {exception.Message}",
                exception
            );
        }

        return
        [
            .. diff.Descendants("path")
                .Select(path => path.Value)
                .Select(path =>
                    path.Contains("://", StringComparison.Ordinal)
                        ? Uri.UnescapeDataString(path)
                        : path.Replace('\\', '/')
                ),
        ];
    }

    /// <returns>What happened to each path, or <see langword="null"/> when the text is not XML.</returns>
    internal static IReadOnlyList<SummarisedPath>? Changes(string xml)
    {
        try
        {
            return
            [
                .. XElement
                    .Parse(xml)
                    .Descendants("path")
                    .Select(path => new SummarisedPath(
                        (string?)path.Attribute("item") ?? string.Empty,
                        (string?)path.Attribute("props") ?? string.Empty,
                        (string?)path.Attribute("kind") ?? string.Empty
                    )),
            ];
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
