using System.Xml;
using System.Xml.Linq;

namespace Subverted.Svn;

/// <summary>Reads <c>svn proplist --verbose --xml</c> for a single target.</summary>
internal static class SvnPropertyListXml
{
    /// <returns>
    /// Every property with its value, empty when there are none, and <see langword="null"/> when
    /// the text is not XML.
    /// </returns>
    public static IReadOnlyList<SvnProperty>? Parse(string xml)
    {
        try
        {
            return
            [
                .. XElement
                    .Parse(xml)
                    .Descendants("property")
                    .Select(property => new SvnProperty(
                        (string?)property.Attribute("name") ?? string.Empty,
                        property.Value
                    )),
            ];
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
